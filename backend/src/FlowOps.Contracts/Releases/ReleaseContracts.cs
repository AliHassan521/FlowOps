using System.ComponentModel.DataAnnotations;
namespace FlowOps.Contracts.Releases;

public sealed record CreateEnvironmentRequest([Required, StringLength(80, MinimumLength = 2)] string Name);
public sealed record ReleaseEnvironmentResponse(Guid Id, string Name, bool IsActive, DateTime CreatedAtUtc);
public sealed record CreateReleaseRequest([Required, StringLength(120, MinimumLength = 2)] string ApplicationName,
    [Required, StringLength(80)] string ReleaseVersion, Guid EnvironmentId, [StringLength(8000)] string? Notes);
public sealed record UpdateReleaseRequest(Guid ExpectedVersion, [StringLength(8000)] string? Notes);
public sealed record SetReleaseLinksRequest(Guid ExpectedVersion, [Required, MaxLength(100)] Guid[] WorkItemIds, [Required, MaxLength(100)] Guid[] IncidentIds);
public sealed record TransitionReleaseRequest(Guid ExpectedVersion, [Required] string Status, [StringLength(4000)] string? Outcome = null);
public sealed record ReleaseListQuery(string? Status = null, Guid? EnvironmentId = null, string? Search = null, int Page = 1, int PageSize = 20);
public sealed record ReleaseResponse(Guid Id, Guid Version, Guid ProjectId, string ApplicationName, string ReleaseVersion,
    Guid EnvironmentId, string EnvironmentName, bool EnvironmentActive, string? Notes, string Status, Guid CreatedByUserId,
    string CreatorName, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, Guid? ApprovedByUserId, DateTime? ApprovedAtUtc,
    DateTime? DeploymentStartedAtUtc, DateTime? CompletedAtUtc, string? Outcome, int WorkItemCount, int IncidentCount);
public sealed record ReleaseResourceResponse(Guid Id, string Title, string Status);
public sealed record ReleaseLinksResponse(IReadOnlyList<ReleaseResourceResponse> WorkItems, IReadOnlyList<ReleaseResourceResponse> Incidents);
public sealed record ReleaseTimelineResponse(Guid Id, Guid? ActorUserId, string ActorName, string Action, DateTime OccurredAtUtc);
