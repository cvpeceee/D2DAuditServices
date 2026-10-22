using System.Reflection;
using System.Text.Json;
using D2DAudit.Application;
using D2DAudit.Domain;

namespace D2DAudit.Infrastructure.Seed;

internal static class ClientSeed
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ClientDocument Document = Load();

    public static ClientCompany Company => Document.Company;

    public static IReadOnlyList<ClientPerson> People => Document.People;

    public static IReadOnlyList<ExampleControlDocument> Controls => Document.Controls;

    public static IReadOnlyList<NormalizedResource> Resources { get; } = Document.Resources
        .Select(item => new NormalizedResource(item.Provider, item.Kind, item.ExternalId, item.Name, item.Attributes))
        .ToArray();

    public static ClientPerson? Person(string email) =>
        Document.People.FirstOrDefault(item => string.Equals(item.Email, email, StringComparison.OrdinalIgnoreCase));

    public static DateOnly Day(DateTimeOffset now, int offset) => DateOnly.FromDateTime(now.UtcDateTime).AddDays(offset);

    public static DateTimeOffset Moment(DateTimeOffset now, int dayOffset) => now.AddDays(dayOffset);

    public static IReadOnlyList<ClientRisk> Risks => Document.Risks;

    public static IReadOnlyList<ClientVendor> Vendors => Document.Vendors;

    public static IReadOnlyList<ClientHistory> History => Document.History;

    public static IReadOnlyList<ClientNote> Notifications => Document.Notifications;

    public static IReadOnlyList<ClientDevice> Devices => Document.Devices;

    public static IReadOnlyList<ClientActivity> Activity => Document.Activity;

    public static IReadOnlyList<ClientFinding> Findings => Document.Findings;

    public static IReadOnlyList<ClientRequest> Requests => Document.Requests;

    public static IReadOnlyList<ClientEvidence> Evidence => Document.Evidence;

    public static ClientAccess AccessReview => Document.AccessReview;

    public static AccessDecision DecisionFor(int index)
    {
        if (index % 11 == 0)
        {
            return AccessDecision.Revoked;
        }

        return index % 5 == 0 ? AccessDecision.Approved : AccessDecision.Pending;
    }

    private static ClientDocument Load()
    {
        var assembly = typeof(ClientSeed).Assembly;
        var name = assembly.GetManifestResourceNames().First(item => item.EndsWith("harborline.json", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing Harborline seed.");
        return JsonSerializer.Deserialize<ClientDocument>(stream, Json) ?? throw new InvalidOperationException("Harborline seed is empty.");
    }
}

internal sealed class ClientDocument
{
    public ClientCompany Company { get; set; } = new();

    public List<ClientPerson> People { get; set; } = [];

    public List<ClientResource> Resources { get; set; } = [];

    public List<ClientRisk> Risks { get; set; } = [];

    public List<ClientVendor> Vendors { get; set; } = [];

    public List<ClientHistory> History { get; set; } = [];

    public List<ClientNote> Notifications { get; set; } = [];

    public List<ClientDevice> Devices { get; set; } = [];

    public List<ClientActivity> Activity { get; set; } = [];

    public List<ClientFinding> Findings { get; set; } = [];

    public List<ClientRequest> Requests { get; set; } = [];

    public List<ClientEvidence> Evidence { get; set; } = [];

    public List<ExampleControlDocument> Controls { get; set; } = [];

    public ClientAccess AccessReview { get; set; } = new();
}

internal sealed class ClientCompany
{
    public string Name { get; set; } = "";

    public string Slug { get; set; } = "";

    public string Domain { get; set; } = "";

    public string Region { get; set; } = "";

    public string Summary { get; set; } = "";
}

internal sealed class ClientPerson
{
    public string Name { get; set; } = "";

    public string Email { get; set; } = "";

    public string Department { get; set; } = "";

    public string Role { get; set; } = "";

    public int HireOffset { get; set; }

    public int? LeaveOffset { get; set; }

    public string Training { get; set; } = "Assigned";

    public int TrainingDueOffset { get; set; }
}

internal sealed class ClientResource
{
    public string Provider { get; set; } = "";

    public string Kind { get; set; } = "";

    public string ExternalId { get; set; } = "";

    public string Name { get; set; } = "";

    public Dictionary<string, string> Attributes { get; set; } = [];
}

internal sealed class ClientRisk
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Statement { get; set; } = "";

    public int Likelihood { get; set; }

    public int Impact { get; set; }

    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public string Treatment { get; set; } = "";

    public string TreatmentPlan { get; set; } = "";

    public int ReviewOffset { get; set; }

    public int ResidualLikelihood { get; set; }

    public int ResidualImpact { get; set; }

    public List<string> ControlCodes { get; set; } = [];
}

internal sealed class ClientVendor
{
    public string Name { get; set; } = "";

    public string Service { get; set; } = "";

    public string Criticality { get; set; } = "";

    public string DataAccess { get; set; } = "";

    public string ReviewState { get; set; } = "";

    public int ReviewOffset { get; set; }

    public int RiskRating { get; set; }

    public List<ClientVendorFile> Documents { get; set; } = [];
}

internal sealed class ClientVendorFile
{
    public string Title { get; set; } = "";

    public string Kind { get; set; } = "";

    public int IssuedOffset { get; set; }
}

internal sealed class ClientHistory
{
    public int Week { get; set; }

    public int Passing { get; set; }

    public int Failing { get; set; }
}

internal sealed class ClientNote
{
    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    public string Href { get; set; } = "";

    public int DayOffset { get; set; }
}

internal sealed class ClientDevice
{
    public string OwnerName { get; set; } = "";

    public string OwnerEmail { get; set; } = "";

    public string Platform { get; set; } = "";

    public string Serial { get; set; } = "";

    public int SeenOffset { get; set; }
}

internal sealed class ClientActivity
{
    public int DayOffset { get; set; }

    public string Action { get; set; } = "";

    public string Result { get; set; } = "";

    public string Detail { get; set; } = "";

    public string Actor { get; set; } = "";
}

internal sealed class ClientFinding
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Severity { get; set; } = "";

    public string Detail { get; set; } = "";

    public string RequirementCode { get; set; } = "";

    public string? ManagementResponse { get; set; }

    public string? ResponseByName { get; set; }

    public List<ClientComment> Comments { get; set; } = [];
}

internal sealed class ClientComment
{
    public string Author { get; set; } = "";

    public int DayOffset { get; set; }

    public string Body { get; set; } = "";
}

internal sealed class ClientRequest
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";

    public string Population { get; set; } = "";

    public int? SampleSize { get; set; }

    public string Source { get; set; } = "";

    public int DueOffset { get; set; }

    public string Status { get; set; } = "";

    public int CreatedOffset { get; set; }

    public string? Response { get; set; }
}

internal sealed class ClientEvidence
{
    public string Control { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";
}

internal sealed class ClientAccess
{
    public string Name { get; set; } = "";

    public int DueOffset { get; set; }

    public int StartedOffset { get; set; }
}
