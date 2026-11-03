using System.Linq.Expressions;
using FlowOps.Contracts.Releases;
using FlowOps.Domain.Entities;
namespace FlowOps.Infrastructure.Services;

internal static class ReleaseReadModel
{
    public static readonly Expression<Func<Release, ReleaseResponse>> Project = r => new ReleaseResponse(
        r.Id, r.Version, r.ProjectId, r.ApplicationName, r.ReleaseVersion, r.EnvironmentId, r.Environment.Name, r.Environment.IsActive,
        r.Notes, r.Status.ToString(), r.CreatedByUserId, r.CreatedBy.FullName, r.CreatedAtUtc, r.UpdatedAtUtc, r.ApprovedByUserId,
        r.ApprovedAtUtc, r.DeploymentStartedAtUtc, r.CompletedAtUtc, r.Outcome, r.WorkItems.Count, r.Incidents.Count);
}
