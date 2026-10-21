using System.Security.Cryptography;
using System.Text;

namespace D2DAudit.Domain;

public static class EvidenceChain
{
    public static string ContentHash(ReadOnlySpan<byte> payload) =>
        Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    public static string Canonical(Guid tenantId, string objectKey, string contentHash, string title) =>
        $"{tenantId:N}|{objectKey}|{contentHash}|{title}";

    public static string NextHead(string? previousHead, string contentHash, string canonical)
    {
        var material = Encoding.UTF8.GetBytes($"{previousHead ?? ""}\n{contentHash}\n{canonical}");
        return ContentHash(material);
    }
}
