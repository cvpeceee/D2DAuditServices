using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using D2DAudit.Application;
using D2DAudit.Domain;
using D2DAudit.Infrastructure.Evidence;
using D2DAudit.Infrastructure.Integrations;
using D2DAudit.Infrastructure.Persistence;
using D2DAudit.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace D2DAudit.Infrastructure.Audits;

public sealed record ExportWork(Guid TenantId, Guid JobId, string[]? FrameworkKeys);

public sealed class AuditExportQueue(IServiceScopeFactory scopes, ILogger<AuditExportQueue> logger) : BackgroundService
{
    private readonly Channel<ExportWork> _pending = Channel.CreateUnbounded<ExportWork>();

    public async Task EnqueueAsync(ExportWork work, CancellationToken cancellationToken) =>
        await _pending.Writer.WriteAsync(work, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<AuditPackageRunner>().RunAsync(work, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Audit package {JobId} failed before the runner finished.", work.JobId);
            }
        }
    }
}

public sealed class AuditPackageRunner(
    ControlPlaneDbContext control,
    TenantDatabases databases,
    EvidenceWriter evidence,
    IStorageProvider storage,
    CollectionJobs jobs,
    TimeProvider time,
    ILogger<AuditPackageRunner> logger)
{
    private const int Steps = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(ExportWork work, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(work, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Audit package {JobId} failed.", work.JobId);
            await jobs.FinishAsync(work.JobId, false, "package_failed", "Package failed", cancellationToken);
        }
    }

    private async Task RunCoreAsync(ExportWork work, CancellationToken cancellationToken)
    {
        var tenant = await control.Tenants.FirstAsync(item => item.Id == work.TenantId, cancellationToken);
        await using var db = databases.Create(tenant.DatabaseName!);
        await jobs.AdvanceAsync(work.JobId, 1, Steps, "Reading controls and evidence", cancellationToken);
        var controls = await db.Controls.AsNoTracking()
            .Include(item => item.Mappings).ThenInclude(mapping => mapping.Requirement!).ThenInclude(requirement => requirement.Framework)
            .Include(item => item.Evidence)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
        var inScope = controls.Where(item => InScope(item, work.FrameworkKeys)).ToList();

        await jobs.AdvanceAsync(work.JobId, 2, Steps, "Checking the hash chain", cancellationToken);
        var report = await evidence.VerifyAsync(db, work.TenantId, cancellationToken);

        await jobs.AdvanceAsync(work.JobId, 3, Steps, "Packing evidence files", cancellationToken);
        var packed = new List<PackedFile>();
        foreach (var controlItem in inScope)
        {
            foreach (var item in controlItem.Evidence.Where(entry => !string.IsNullOrWhiteSpace(entry.ObjectKey)))
            {
                var body = await storage.GetAsync(item.ObjectKey!, cancellationToken);
                var parts = item.ObjectKey!.Split('/');
                var name = parts[^1];
                packed.Add(new PackedFile($"evidence/{controlItem.Code}/{name}", body, EvidenceChain.ContentHash(body), item.Sha256 ?? "", controlItem.Code, item.Title));
            }
        }

        await jobs.AdvanceAsync(work.JobId, 4, Steps, "Writing the manifest and control matrix", cancellationToken);
        var generatedAt = time.GetUtcNow();
        var manifest = new PackageManifest(
            work.TenantId.ToString("N"),
            generatedAt,
            report.Intact,
            report.Checked,
            report.Reason,
            packed.Select(file => new PackageManifestFile(file.Path, file.Sha256, file.RecordedSha256, file.ControlCode, file.Title)).ToArray());
        var matrix = MatrixRows(inScope);
        var zip = BuildZip(manifest, matrix, packed);
        var key = $"{work.TenantId:N}/exports/{work.JobId:N}.zip";

        await jobs.AdvanceAsync(work.JobId, 5, Steps, "Storing the package", cancellationToken);
        await storage.PutAsync(key, zip, "application/zip", null, cancellationToken);
        await jobs.FinishAsync(work.JobId, true, null, "Package ready", cancellationToken);
    }

    private static bool InScope(Control control, string[]? frameworkKeys)
    {
        if (frameworkKeys is null)
        {
            return true;
        }

        return control.Mappings.Any(mapping =>
            mapping.Requirement?.Framework?.Key is string key
            && frameworkKeys.Contains(key, StringComparer.Ordinal));
    }

    private static IReadOnlyList<MatrixRow> MatrixRows(IReadOnlyList<Control> controls) =>
        controls.OrderBy(item => item.Code, StringComparer.Ordinal).Select(item => new MatrixRow(
            item.Code,
            item.Title,
            item.Status.ToString(),
            string.Join(" ", item.Mappings.Select(mapping => mapping.Requirement?.Code).Where(code => code is not null).Distinct().Order(StringComparer.Ordinal)),
            item.Evidence.Count,
            string.Join(" ", item.Evidence.Select(entry => entry.Sha256).Where(hash => !string.IsNullOrWhiteSpace(hash))))).ToArray();

    private static byte[] BuildZip(PackageManifest manifest, IReadOnlyList<MatrixRow> matrix, IReadOnlyList<PackedFile> files)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
            Write(zip, "integrity-report.json", JsonSerializer.SerializeToUtf8Bytes(new
            {
                manifest.Intact,
                manifest.Checked,
                manifest.Reason,
            }, Json));
            Write(zip, "control-matrix.csv", Encoding.UTF8.GetBytes(Csv(matrix)));
            Write(zip, "control-matrix.pdf", PlainPdf.FromLines(PdfLines(matrix)));
            foreach (var file in files)
            {
                Write(zip, file.Path, file.Body);
            }
        }

        return buffer.ToArray();
    }

    private static void Write(ZipArchive zip, string name, byte[] body)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        stream.Write(body);
    }

    private static string Csv(IReadOnlyList<MatrixRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("code,title,status,requirements,evidenceCount,sha256");
        foreach (var row in rows)
        {
            builder.Append(Cell(row.Code)).Append(',')
                .Append(Cell(row.Title)).Append(',')
                .Append(Cell(row.Status)).Append(',')
                .Append(Cell(row.Requirements)).Append(',')
                .Append(row.EvidenceCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Cell(row.Hashes))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Cell(string value)
    {
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{escaped}\"";
    }

    private static IReadOnlyList<string> PdfLines(IReadOnlyList<MatrixRow> rows)
    {
        var lines = new List<string> { "D2D Audit Services control matrix", "" };
        lines.AddRange(rows.Select(row => $"{row.Code}  {row.Status}  {row.Title}"));
        return lines;
    }

    private sealed record PackedFile(string Path, byte[] Body, string Sha256, string RecordedSha256, string ControlCode, string Title);

    private sealed record MatrixRow(string Code, string Title, string Status, string Requirements, int EvidenceCount, string Hashes);
}

public sealed record PackageManifest(
    string TenantId,
    DateTimeOffset GeneratedAt,
    bool Intact,
    int Checked,
    string? Reason,
    IReadOnlyList<PackageManifestFile> Files);

public sealed record PackageManifestFile(string Path, string Sha256, string RecordedSha256, string ControlCode, string Title);

internal static class PlainPdf
{
    public static byte[] FromLines(IReadOnlyList<string> lines)
    {
        var commands = new StringBuilder();
        commands.Append("BT\n/F1 9 Tf\n40 780 Td\n11 TL\n");
        var first = true;
        foreach (var line in lines.Take(60))
        {
            if (!first)
            {
                commands.Append("T*\n");
            }

            commands.Append('(').Append(Ascii(line)).Append(") Tj\n");
            first = false;
        }

        commands.Append("ET\n");
        var stream = Encoding.ASCII.GetBytes(commands.ToString());
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Count 1 /Kids [3 0 R] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var builder = new StringBuilder();
        builder.Append("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (var index = 0; index < objects.Length; index++)
        {
            offsets[index] = Encoding.ASCII.GetByteCount(builder.ToString());
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append(offset.ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        builder.Append("trailer << /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture)).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static string Ascii(string value)
    {
        var chars = value.Select(character => character is >= (char)32 and <= (char)126 && character is not '(' and not ')' and not '\\' ? character : ' ').ToArray();
        var text = new string(chars);
        return text.Length <= 100 ? text : text[..100];
    }
}
