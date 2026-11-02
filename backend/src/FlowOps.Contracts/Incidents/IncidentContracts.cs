using System.ComponentModel.DataAnnotations;
namespace FlowOps.Contracts.Incidents;

public sealed record CreateIncidentRequest(
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(8000)] string? Description,
    [Required, StringLength(120, MinimumLength = 2)] string AffectedService,
    [Required] string Severity, Guid? OwnerUserId = null);
public sealed record UpdateIncidentRequest(Guid ExpectedVersion,
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(8000)] string? Description,
    [Required, StringLength(120, MinimumLength = 2)] string AffectedService, [Required] string Severity);
public sealed record AssignIncidentRequest(Guid ExpectedVersion, Guid? OwnerUserId);
public sealed record TransitionIncidentRequest(Guid ExpectedVersion, [Required] string Status,
    [StringLength(4000)] string? Resolution = null, [StringLength(4000)] string? RootCause = null);
public sealed record AddIncidentCommentRequest([Required, StringLength(4000, MinimumLength = 1)] string Body);
public sealed record AttachIncidentSlaRequest(Guid ExpectedVersion, Guid PolicyId);
public sealed record IncidentListQuery(string? Severity = null, string? Status = null, Guid? OwnerUserId = null,
    string? Search = null, int Page = 1, int PageSize = 20);
public sealed record IncidentResponse(Guid Id, string Number, Guid Version, Guid ProjectId, string Title,
    string? Description, string AffectedService, string Severity, string Status, Guid ReporterUserId, string ReporterName,
    Guid? OwnerUserId, string? OwnerName, DateTime StartedAtUtc, DateTime UpdatedAtUtc, DateTime? EndedAtUtc,
    Guid? ResolvedByUserId, string? Resolution, string? RootCause);
public sealed record IncidentCommentResponse(Guid Id, Guid AuthorUserId, string AuthorName, string Body, DateTime CreatedAtUtc);
public sealed record IncidentTimelineResponse(Guid Id, Guid? ActorUserId, string ActorName, string Action, string? DetailsJson, DateTime OccurredAtUtc);
public sealed record IncidentSummaryResponse(long ActiveCount, long CriticalCount, long ResolvedCount, long AtRiskCount, long BreachedCount);
public sealed record IncidentSlaResponse(Guid Id, Guid PolicyId, string PolicyName, int DurationMinutes,
    int WarningBeforeMinutes, DateTime StartedAtUtc, DateTime DeadlineAtUtc, DateTime? WarningAtUtc,
    DateTime? WarnedAtUtc, DateTime? BreachedAtUtc, DateTime? CompletedAtUtc, string Status, long RemainingSeconds);
public sealed record IncidentSlaStateResponse(IncidentSlaResponse? Sla, DateTime ServerTimeUtc);
