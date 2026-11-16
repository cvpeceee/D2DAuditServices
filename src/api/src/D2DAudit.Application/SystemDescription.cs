namespace D2DAudit.Application;

public sealed record SystemDescriptionDto(string Body, DateTimeOffset UpdatedAt, string UpdatedBy);

public sealed record SaveSystemDescriptionRequest(string Body);

public interface ISystemDescription
{
    Task<SystemDescriptionDto> GetAsync(CancellationToken cancellationToken);

    Task<Result<SystemDescriptionDto>> SaveAsync(SaveSystemDescriptionRequest request, CancellationToken cancellationToken);

    Task<Result<SystemDescriptionDto>> RedraftAsync(CancellationToken cancellationToken);
}
