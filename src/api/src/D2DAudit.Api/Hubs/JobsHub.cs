using D2DAudit.Application;
using D2DAudit.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace D2DAudit.Api.Hubs;

[Authorize]
public sealed class JobsHub(IControlPlaneDirectory directory, TimeProvider time) : Hub
{
    public async Task Subscribe(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var id))
        {
            throw new HubException("forbidden");
        }

        var subject = Context.User?.FindFirst("sub")?.Value;
        if (subject is null)
        {
            throw new HubException("forbidden");
        }

        var user = await directory.FindUserBySubjectAsync(subject, Context.ConnectionAborted);
        var memberships = user is null
            ? []
            : await directory.MembershipsAsync(user.Id, Context.ConnectionAborted);
        var membership = memberships.FirstOrDefault(item => item.TenantId == id && item.Status == TenantStatus.Active);
        if (user is null || membership is null)
        {
            throw new HubException("forbidden");
        }

        if (membership.Role == TenantRole.Auditor)
        {
            var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            var grants = await directory.ActiveGrantsAsync(user.Id, today, Context.ConnectionAborted);
            if (grants.All(grant => grant.TenantId != id))
            {
                throw new HubException("forbidden");
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(id));
    }

    public static string Group(Guid tenantId) => "tenant:" + tenantId.ToString("N");
}

public sealed class SignalRJobNotifier(IHubContext<JobsHub> hub) : IJobNotifier
{
    public Task JobChangedAsync(JobRecord job, CancellationToken cancellationToken)
    {
        if (job.TenantId is null)
        {
            return Task.CompletedTask;
        }

        var dto = new JobDto(
            job.Id,
            job.Kind,
            job.Status.ToString(),
            job.Step,
            job.TotalSteps,
            job.CurrentStep,
            job.Percent,
            job.Error,
            job.UpdatedAt);
        return hub.Clients.Group(JobsHub.Group(job.TenantId.Value)).SendAsync("job", dto, cancellationToken);
    }
}
