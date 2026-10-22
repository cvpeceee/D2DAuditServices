using System.Reflection;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Infrastructure.Seed;

namespace D2DAudit.Infrastructure.Catalog;

public sealed class FrameworkCatalog : IFrameworkCatalog
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public FrameworkCatalog()
    {
        var assembly = typeof(FrameworkCatalog).Assembly;
        var names = assembly.GetManifestResourceNames();
        var frameworks = new List<FrameworkDocument>();
        ExampleCatalogDocument? example = null;
        foreach (var name in names.Where(name => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
        {
            if (name.EndsWith("client.json", StringComparison.OrdinalIgnoreCase) || name.EndsWith("harborline.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing embedded framework content.");
            if (name.EndsWith("example-controls.json", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd()
                    .Replace("{{company}}", ClientSeed.Company.Name, StringComparison.Ordinal)
                    .Replace("{{domain}}", ClientSeed.Company.Domain, StringComparison.Ordinal);
                example = JsonSerializer.Deserialize<ExampleCatalogDocument>(json, Json)
                    ?? throw new InvalidOperationException("Example control catalog is empty.");
            }
            else
            {
                var document = JsonSerializer.Deserialize<FrameworkDocument>(stream, Json)
                    ?? throw new InvalidOperationException($"Framework content {name} is empty.");
                frameworks.Add(document);
            }
        }

        Frameworks = frameworks.OrderBy(framework => framework.Key, StringComparer.Ordinal).ToArray();
        ExampleControls = example ?? throw new InvalidOperationException("Example control catalog was not embedded.");
        var known = ExampleControls.Controls.Select(control => control.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var control in ClientSeed.Controls.Where(control => known.Add(control.Code)))
        {
            ExampleControls.Controls.Add(control);
        }
        if (Frameworks.Count == 0)
        {
            throw new InvalidOperationException("No framework content was embedded.");
        }
    }

    public IReadOnlyList<FrameworkDocument> Frameworks { get; }

    public ExampleCatalogDocument ExampleControls { get; }
}
