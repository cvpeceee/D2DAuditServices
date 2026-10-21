using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using D2DAudit.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Storage;

public sealed class SecretBox(IConfiguration configuration)
{
    public bool CanProtect => Key() is not null;

    public string? Encrypt(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return null;
        }

        var key = Key();
        if (key is null)
        {
            return null;
        }

        var plain = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public string? Decrypt(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return null;
        }

        var key = Key() ?? throw new InvalidOperationException("TENANT_DATA_KEY is required to read a stored secret.");
        var bytes = Convert.FromBase64String(stored);
        var nonce = bytes[..12];
        var tag = bytes[12..28];
        var cipher = bytes[28..];
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }

    private byte[]? Key()
    {
        var raw = configuration["TENANT_DATA_KEY"];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var key = Convert.FromBase64String(raw);
        return key.Length == 32 ? key : throw new InvalidOperationException("TENANT_DATA_KEY must be 32 bytes, base64 encoded.");
    }
}

public sealed class LocalStorageProvider(IConfiguration configuration) : IStorageProvider
{
    public async Task PutAsync(string key, ReadOnlyMemory<byte> body, string contentType, DateTimeOffset? lockUntil, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        var sidecar = path + ".lock";
        if (File.Exists(sidecar)
            && DateTimeOffset.TryParse(await File.ReadAllTextAsync(sidecar, cancellationToken), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until)
            && until > DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Object lock refused an overwrite.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, body.ToArray(), cancellationToken);
        if (lockUntil is not null)
        {
            await File.WriteAllTextAsync(sidecar, lockUntil.Value.ToString("O"), cancellationToken);
        }

        _ = contentType;
    }

    public Task<byte[]> GetAsync(string key, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(PathFor(key), cancellationToken);

    private string PathFor(string key)
    {
        if (key.Contains("..", StringComparison.Ordinal) || key.StartsWith('/') || key.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing an unsafe object key.");
        }

        var root = configuration["Storage:Root"];
        if (string.IsNullOrWhiteSpace(root))
        {
            root = "data/evidence";
        }

        return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}

public sealed class S3StorageProvider(IConfiguration configuration, ILogger<S3StorageProvider> logger) : IStorageProvider
{
    public async Task PutAsync(string key, ReadOnlyMemory<byte> body, string contentType, DateTimeOffset? lockUntil, CancellationToken cancellationToken)
    {
        Reject(key);
        using var client = Create();
        var bucket = Bucket();
        await EnsureBucketAsync(client, bucket, cancellationToken);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            ContentType = contentType,
            InputStream = new MemoryStream(body.ToArray()),
        };
        if (ObjectLock() && lockUntil is not null)
        {
            request.ObjectLockMode = ObjectLockMode.Governance;
            request.ObjectLockRetainUntilDate = lockUntil.Value.UtcDateTime;
        }

        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<byte[]> GetAsync(string key, CancellationToken cancellationToken)
    {
        Reject(key);
        using var client = Create();
        using var response = await client.GetObjectAsync(Bucket(), key, cancellationToken);
        using var memory = new MemoryStream();
        await response.ResponseStream.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private AmazonS3Client Create()
    {
        var endpoint = configuration["Storage:Endpoint"];
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(configuration["Storage:Region"] ?? "us-east-1"),
            ForcePathStyle = !string.Equals(configuration["Storage:ForcePathStyle"], "false", StringComparison.OrdinalIgnoreCase),
        };
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            config.ServiceURL = endpoint;
        }

        var accessKey = configuration["Storage:AccessKey"];
        var secret = configuration["Storage:SecretKey"];
        if (!string.IsNullOrWhiteSpace(accessKey) && !string.IsNullOrWhiteSpace(secret))
        {
            return new AmazonS3Client(accessKey, secret, config);
        }

        return new AmazonS3Client(config);
    }

    private string Bucket() => configuration["Storage:Bucket"] ?? "d2d-evidence";

    private bool ObjectLock() => string.Equals(configuration["Storage:ObjectLock"], "true", StringComparison.OrdinalIgnoreCase);

    private async Task EnsureBucketAsync(AmazonS3Client client, string bucket, CancellationToken cancellationToken)
    {
        try
        {
            await client.PutBucketAsync(new PutBucketRequest
            {
                BucketName = bucket,
                ObjectLockEnabledForBucket = ObjectLock(),
            }, cancellationToken);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            logger.LogDebug(exception, "Evidence bucket already exists.");
        }
    }

    private static void Reject(string key)
    {
        if (key.Contains("..", StringComparison.Ordinal) || key.StartsWith('/') || key.Contains('\\', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Refusing an unsafe object key.");
        }
    }
}
