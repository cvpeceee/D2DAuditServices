namespace D2DAudit.Application;

public sealed record ImpactItem(Guid Id, string Name, string Criticality, int RtoHours, int RpoHours, string OwnerName, string ControlCode, string Notes);

public sealed record PlanItem(Guid Id, string Name, string Kind, string Body, string ControlCode, DateTimeOffset UpdatedAt);

public sealed record ExerciseItem(Guid Id, string Name, Guid? PlanId, string Result, string Notes, string ControlCode, DateTimeOffset RanAt);

public sealed record ContinuityBoard(IReadOnlyList<ImpactItem> Services, IReadOnlyList<PlanItem> Plans, IReadOnlyList<ExerciseItem> Exercises);

public sealed record SaveImpactRequest(string Name, string Criticality, int RtoHours, int RpoHours, string OwnerName, string ControlCode, string Notes);

public sealed record SavePlanRequest(string Name, string Kind, string Body, string ControlCode);

public sealed record SaveExerciseRequest(string Name, Guid? PlanId, string Result, string Notes, string ControlCode);

public interface IContinuity
{
    Task<ContinuityBoard> GetAsync(CancellationToken cancellationToken);

    Task<Result<ImpactItem>> SaveServiceAsync(SaveImpactRequest request, CancellationToken cancellationToken);

    Task<Result<PlanItem>> SavePlanAsync(SavePlanRequest request, CancellationToken cancellationToken);

    Task<Result<ExerciseItem>> SaveExerciseAsync(SaveExerciseRequest request, CancellationToken cancellationToken);
}
