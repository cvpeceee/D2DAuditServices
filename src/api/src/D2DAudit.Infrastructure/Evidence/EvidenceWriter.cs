using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.S3;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Parquet;
using Parquet.Schema;

namespace D2DAudit.Infrastructure.Evidence;

public sealed class EvidenceWriter(IStorageProvider storage, TimeProvider time, ILogger<EvidenceWriter> logger)
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _tenantGates = new();

    public async Task AppendAsync(
        TenantDbContext db,
        Guid tenantId,
        EvidenceItem item,
        byte[] body,
        string contentType,
        DateTimeOffset lockedUntil,
        CancellationToken cancellationToken)
    {
        var gate = _tenantGates.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var hash = EvidenceChain.ContentHash(body);
            var last = await db.Chain
                .AsNoTracking()
                .OrderByDescending(entry => entry.Sequence)
                .Select(entry => new { entry.Sequence, entry.Head })
                .FirstOrDefaultAsync(cancellationToken);
            var localLast = db.Chain.Local
                .OrderByDescending(entry => entry.Sequence)
                .FirstOrDefault();
            var sequence = Math.Max(last?.Sequence ?? 0, localLast?.Sequence ?? 0);
            var previous = localLast is not null && localLast.Sequence >= (last?.Sequence ?? 0)
                ? localLast.Head
                : last?.Head;
            var canonical = EvidenceChain.Canonical(tenantId, item.ObjectKey!, hash, item.Title);
            var head = EvidenceChain.NextHead(previous, hash, canonical);
            await storage.PutAsync(item.ObjectKey!, body, contentType, lockedUntil, cancellationToken);
            var now = time.GetUtcNow();
            db.Chain.Add(new ChainEntry
            {
                Id = Guid.NewGuid(),
                Sequence = sequence + 1,
                EvidenceId = item.Id,
                ObjectKey = item.ObjectKey!,
                ContentHash = hash,
                PreviousHead = previous,
                Head = head,
                Canonical = canonical,
                CreatedAt = now,
            });
            item.Sha256 = hash;
            item.PreviousHead = previous;
            item.ChainHead = head;
            item.Integrity = EvidenceIntegrity.Verified;
            item.LockedUntil = lockedUntil;
            item.CollectedAt = now;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<VerifyReport> VerifyAsync(TenantDbContext db, Guid tenantId, CancellationToken cancellationToken)
    {
        var entries = await db.Chain.OrderBy(entry => entry.Sequence).ToListAsync(cancellationToken);
        string? previous = null;
        var checkedCount = 0;
        foreach (var entry in entries)
        {
            checkedCount++;
            if (!entry.ObjectKey.StartsWith(tenantId.ToString("N") + "/", StringComparison.Ordinal))
            {
                return new VerifyReport(false, checkedCount, entry.ObjectKey, "object_outside_tenant");
            }

            byte[] body;
            try
            {
                body = await storage.GetAsync(entry.ObjectKey, cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or AmazonS3Exception or InvalidOperationException)
            {
                logger.LogDebug(exception, "Evidence bytes could not be read during chain verification.");
                return new VerifyReport(false, checkedCount, entry.ObjectKey, "object_missing");
            }

            var hash = EvidenceChain.ContentHash(body);
            if (!string.Equals(hash, entry.ContentHash, StringComparison.Ordinal))
            {
                return new VerifyReport(false, checkedCount, entry.ObjectKey, "content_hash");
            }

            var head = EvidenceChain.NextHead(previous, entry.ContentHash, entry.Canonical);
            if (!string.Equals(head, entry.Head, StringComparison.Ordinal) || !string.Equals(previous, entry.PreviousHead, StringComparison.Ordinal))
            {
                return new VerifyReport(false, checkedCount, entry.ObjectKey, "chain");
            }

            previous = entry.Head;
        }

        return new VerifyReport(true, checkedCount, null, null);
    }

    public static async Task<byte[]> ParquetAsync(IReadOnlyList<NormalizedResource> resources, CancellationToken cancellationToken)
    {
        var schema = new ParquetSchema(
            new DataField<string>("provider"),
            new DataField<string>("kind"),
            new DataField<string>("external_id"),
            new DataField<string>("name"),
            new DataField<string>("attributes"));
        using var stream = new MemoryStream();
        await using (var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: cancellationToken))
        {
            using var group = writer.CreateRowGroup();
            await group.WriteAsync(schema.DataFields[0], resources.Select(item => item.Provider).ToArray());
            await group.WriteAsync(schema.DataFields[1], resources.Select(item => item.Kind).ToArray());
            await group.WriteAsync(schema.DataFields[2], resources.Select(item => item.ExternalId).ToArray());
            await group.WriteAsync(schema.DataFields[3], resources.Select(item => item.Name).ToArray());
            await group.WriteAsync(schema.DataFields[4], resources.Select(item => JsonSerializer.Serialize(item.Attributes)).ToArray());
            cancellationToken.ThrowIfCancellationRequested();
        }

        return stream.ToArray();
    }
}
