namespace FlowOps.Domain.Sla;

public sealed record SlaSchedule(DateTime StartedAtUtc, DateTime DeadlineAtUtc, DateTime? WarningAtUtc)
{
    public static SlaSchedule Calculate(DateTime startedAtUtc, int durationMinutes, int warningBeforeMinutes)
    {
        if (startedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("SLA timestamps must use UTC.");
        if (durationMinutes is < 1 or > 43200)
            throw new ArgumentException("SLA duration must be between 1 and 43200 minutes.");
        if (warningBeforeMinutes < 0 || warningBeforeMinutes >= durationMinutes)
            throw new ArgumentException("Warning lead time must be zero or shorter than the SLA duration.");
        var deadline = startedAtUtc.AddMinutes(durationMinutes);
        return new(startedAtUtc, deadline, warningBeforeMinutes == 0 ? null : deadline.AddMinutes(-warningBeforeMinutes));
    }
}
