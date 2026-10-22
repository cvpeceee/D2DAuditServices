using System.Text.RegularExpressions;
using D2DAudit.Domain;
using FluentValidation;

namespace D2DAudit.Application;

public sealed partial class CreateTenantValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantValidator()
    {
        RuleFor(request => request.Slug).NotEmpty().Matches(Slug());
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Region).Must(region => region is "US" or "EU");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,62}$")]
    private static partial Regex Slug();
}

public sealed partial class AssignOwnerValidator : AbstractValidator<AssignOwnerRequest>
{
    public AssignOwnerValidator()
    {
        RuleFor(request => request.OwnerName).MaximumLength(200);
        RuleFor(request => request.OwnerEmail).Must(BeEmailOrEmpty);
    }

    private static bool BeEmailOrEmpty(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return true;
        }

        var value = email.Trim();
        return value.Length <= 320 && Email().IsMatch(value);
    }

    [GeneratedRegex("^[^\r\n@\\s]+@[^\r\n@\\s]+\\.[^\r\n@\\s]+$")]
    private static partial Regex Email();
}

public sealed class SaveRiskValidator : AbstractValidator<SaveRiskRequest>
{
    public SaveRiskValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Statement).MaximumLength(2000);
        RuleFor(request => request.Likelihood).InclusiveBetween(RiskScoring.ScaleMin, RiskScoring.ScaleMax);
        RuleFor(request => request.Impact).InclusiveBetween(RiskScoring.ScaleMin, RiskScoring.ScaleMax);
        RuleFor(request => request.ResidualLikelihood).InclusiveBetween(RiskScoring.ScaleMin, RiskScoring.ScaleMax);
        RuleFor(request => request.ResidualImpact).InclusiveBetween(RiskScoring.ScaleMin, RiskScoring.ScaleMax);
        RuleFor(request => request.OwnerName).NotEmpty().MaximumLength(200);
        RuleFor(request => request.OwnerEmail).NotEmpty().MaximumLength(320);
        RuleFor(request => request.Treatment).Must(value => Enum.TryParse<RiskTreatment>(value, out _));
        RuleFor(request => request.TreatmentPlan).MaximumLength(2000);
        RuleFor(request => request.ControlCodes).NotNull();
    }
}

public sealed class SaveSoaValidator : AbstractValidator<SaveSoaRequest>
{
    public SaveSoaValidator()
    {
        RuleFor(request => request.Justification).NotEmpty().MaximumLength(2000);
        RuleFor(request => request.Implementation).Must(value => Enum.TryParse<SoaImplementation>(value, out _));
    }
}

public sealed class SaveVendorValidator : AbstractValidator<SaveVendorRequest>
{
    public SaveVendorValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Service).NotEmpty().MaximumLength(200);
        RuleFor(request => request.Criticality).Must(value => Enum.TryParse<VendorCriticality>(value, out _));
        RuleFor(request => request.DataAccess).MaximumLength(1000);
        RuleFor(request => request.ReviewState).Must(value => Enum.TryParse<VendorReviewState>(value, out _));
        RuleFor(request => request.RiskRating).InclusiveBetween(RiskScoring.ScaleMin, RiskScoring.ScaleMax);
        RuleFor(request => request.Documents).NotNull();
    }
}

public sealed class StartCampaignValidator : AbstractValidator<StartCampaignRequest>
{
    public StartCampaignValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(200);
    }
}
