using FlowOps.Domain.Entities;
using FlowOps.Domain.Sla;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class SlaRulesTests
{
    private static readonly DateTime Start = new(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Schedule_calculates_deadline_and_warning_in_UTC()
    {
        var schedule = SlaSchedule.Calculate(Start, 240, 60);
        Assert.Equal(Start.AddHours(4), schedule.DeadlineAtUtc);
        Assert.Equal(Start.AddHours(3), schedule.WarningAtUtc);
        Assert.Equal(DateTimeKind.Utc, schedule.DeadlineAtUtc.Kind);
        Assert.Null(SlaSchedule.Calculate(Start, 240, 0).WarningAtUtc);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(43201, 60)]
    [InlineData(240, -1)]
    [InlineData(240, 240)]
    [InlineData(240, 300)]
    public void Invalid_durations_and_warning_thresholds_are_rejected(int duration, int warning) =>
        Assert.Throws<ArgumentException>(() => SlaSchedule.Calculate(Start, duration, warning));

    [Fact]
    public void Non_UTC_start_is_rejected() =>
        Assert.Throws<ArgumentException>(() => SlaSchedule.Calculate(DateTime.SpecifyKind(Start, DateTimeKind.Unspecified), 240, 60));

    [Fact]
    public void Warning_and_breach_boundaries_are_single_use()
    {
        var sla = StartSla();
        Assert.False(sla.TryRecordWarning(Start.AddMinutes(179)));
        Assert.True(sla.TryRecordWarning(Start.AddMinutes(180)));
        Assert.False(sla.TryRecordWarning(Start.AddMinutes(181)));
        Assert.False(sla.TryRecordBreach(Start.AddMinutes(239)));
        Assert.True(sla.TryRecordBreach(Start.AddMinutes(240)));
        Assert.False(sla.TryRecordBreach(Start.AddMinutes(300)));
        Assert.Equal(Start.AddMinutes(240), sla.BreachedAtUtc);
    }

    [Fact]
    public void Missed_warning_is_not_emitted_after_the_deadline()
    {
        var sla = StartSla();
        Assert.False(sla.TryRecordWarning(Start.AddHours(5)));
        Assert.True(sla.TryRecordBreach(Start.AddHours(5)));
        Assert.Null(sla.WarnedAtUtc);
    }

    [Theory]
    [InlineData(239, false)]
    [InlineData(240, true)]
    [InlineData(300, true)]
    public void Completion_preserves_late_breaches_and_never_restarts(int completedMinutes, bool breached)
    {
        var sla = StartSla();
        Assert.Equal(breached, sla.Complete(Start.AddMinutes(completedMinutes)));
        var completed = sla.CompletedAtUtc;
        Assert.False(sla.Complete(Start.AddMinutes(400)));
        Assert.Equal(completed, sla.CompletedAtUtc);
        Assert.False(sla.TryRecordWarning(Start.AddMinutes(400)));
        Assert.False(sla.TryRecordBreach(Start.AddMinutes(400)));
    }

    private static WorkItemSla StartSla() => WorkItemSla.Start(Guid.NewGuid(),
        new SlaPolicy { Name = "Critical", DurationMinutes = 240, WarningBeforeMinutes = 60 }, Start);
}
