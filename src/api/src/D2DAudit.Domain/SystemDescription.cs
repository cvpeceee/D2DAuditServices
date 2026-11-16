namespace D2DAudit.Domain;

public sealed class SystemDescription
{
    public Guid Id { get; set; }

    public string Body { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = "";
}

public static class SystemDescriptionDraft
{
    public static string Compose(
        string company,
        string region,
        string product,
        IReadOnlyList<string> frameworks,
        int controls,
        int passing,
        IReadOnlyList<string> services,
        int people,
        int vendors)
    {
        var name = string.IsNullOrWhiteSpace(company) ? "The company" : company.Trim();
        var place = string.IsNullOrWhiteSpace(region) ? "its operating region" : region.Trim();
        var productLine = string.IsNullOrWhiteSpace(product)
            ? "The description covers the service the company operates for its customers."
            : product.Trim();
        var frameworkList = frameworks.Count == 0 ? "the frameworks loaded for this workspace" : string.Join(", ", frameworks);
        var serviceList = services.Count == 0 ? "no connected service is listed yet" : string.Join(", ", services);
        return string.Join(
            "\n\n",
            $"{name} prepares this description for its SOC 2 examination. The company operates in {place}. {productLine}",
            $"The system is the service {name} provides to customers, together with the people, vendors, and connected systems that support it. Connected services: {serviceList}.",
            $"{people} people and {vendors} vendors are on record. {passing} of {controls} controls are passing. Frameworks in the workspace: {frameworkList}.",
            "This draft is generated from the workspace. An editor replaces any sentence that does not match how the system actually runs before the description goes into the audit package.");
    }
}
