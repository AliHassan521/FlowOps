using FlowOps.Domain.Sla;

namespace FlowOps.Domain.Entities;

public sealed class WorkItemSla
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Version { get; set; } = Guid.NewGuid();
    public Guid WorkItemId { get; set; }
    public WorkItem WorkItem { get; set; } = null!;
    public Guid PolicyId { get; set; }
    public SlaPolicy Policy { get; set; } = null!;
    public string PolicyName { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int WarningBeforeMinutes { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime DeadlineAtUtc { get; set; }
    public DateTime? WarningAtUtc { get; set; }
    public DateTime? WarnedAtUtc { get; private set; }
    public DateTime? BreachedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static WorkItemSla Start(Guid workItemId, SlaPolicy policy, DateTime now)
    {
        var schedule = SlaSchedule.Calculate(now, policy.DurationMinutes, policy.WarningBeforeMinutes);
        return new() { WorkItemId = workItemId, PolicyId = policy.Id, PolicyName = policy.Name,
            DurationMinutes = policy.DurationMinutes, WarningBeforeMinutes = policy.WarningBeforeMinutes,
            StartedAtUtc = schedule.StartedAtUtc, DeadlineAtUtc = schedule.DeadlineAtUtc, WarningAtUtc = schedule.WarningAtUtc };
    }

    public bool TryRecordWarning(DateTime now)
    {
        if (CompletedAtUtc.HasValue || BreachedAtUtc.HasValue || WarnedAtUtc.HasValue
            || !WarningAtUtc.HasValue || now < WarningAtUtc || now >= DeadlineAtUtc) return false;
        WarnedAtUtc = now;
        return true;
    }

    public bool TryRecordBreach(DateTime now)
    {
        if (CompletedAtUtc.HasValue || BreachedAtUtc.HasValue || now < DeadlineAtUtc) return false;
        BreachedAtUtc = DeadlineAtUtc;
        return true;
    }

    public bool Complete(DateTime now)
    {
        if (CompletedAtUtc.HasValue) return false;
        var newlyBreached = TryRecordBreach(now);
        CompletedAtUtc = now;
        return newlyBreached;
    }
}
