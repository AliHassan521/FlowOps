using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Sla;

public sealed record CreateSlaPolicyRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [Range(1, 43200)] int DurationMinutes,
    [Range(0, 43199)] int WarningBeforeMinutes);

public sealed record SlaPolicyResponse(Guid Id, string Name, int DurationMinutes, int WarningBeforeMinutes,
    bool IsActive, DateTime CreatedAtUtc);

public sealed record AttachWorkItemSlaRequest(Guid PolicyId);

public sealed record WorkItemSlaResponse(Guid Id, Guid PolicyId, string PolicyName, int DurationMinutes,
    int WarningBeforeMinutes, DateTime StartedAtUtc, DateTime DeadlineAtUtc, DateTime? WarningAtUtc,
    DateTime? WarnedAtUtc, DateTime? BreachedAtUtc, DateTime? CompletedAtUtc, string Status, long RemainingSeconds);

public sealed record WorkItemSlaStateResponse(WorkItemSlaResponse? Sla, DateTime ServerTimeUtc);

public sealed record SlaSummaryResponse(long ActiveCount, long AtRiskCount, long BreachedCount, long MetCount);
