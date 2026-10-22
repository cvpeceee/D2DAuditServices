using System.Text.RegularExpressions;
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
