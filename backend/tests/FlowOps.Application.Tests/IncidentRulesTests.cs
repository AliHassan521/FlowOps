using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Domain.Incidents;
using Xunit;
namespace FlowOps.Application.Tests;

public sealed class IncidentRulesTests
{
    [Fact]
    public void Every_lifecycle_edge_is_explicit_and_all_other_pairs_are_rejected()
    {
        foreach (var from in Enum.GetValues<IncidentStatus>())
            foreach (var to in Enum.GetValues<IncidentStatus>())
                Assert.Equal((int)to == (int)from + 1, IncidentWorkflow.CanTransition(from, to));
    }
    [Fact]
    public void Investigation_requires_an_owner_and_resolution_is_immutable_after_completion()
    {
        var now = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc); var actor = Guid.NewGuid();
        var item = new Incident { StartedAtUtc = now };
        Assert.Throws<InvalidOperationException>(() => item.TransitionTo(IncidentStatus.Investigating, actor, null, null, now));
        item.SetOwner(actor, now); item.TransitionTo(IncidentStatus.Investigating, actor, null, null, now);
        Assert.Throws<InvalidOperationException>(() => item.SetOwner(null, now));
        item.TransitionTo(IncidentStatus.Mitigated, actor, null, null, now);
        Assert.Throws<ArgumentException>(() => item.TransitionTo(IncidentStatus.Resolved, actor, " ", null, now));
        Assert.Throws<ArgumentException>(() => item.TransitionTo(IncidentStatus.Resolved, actor, "Fixed", null, now.AddSeconds(-1)));
        item.TransitionTo(IncidentStatus.Resolved, actor, "  Rollback completed  ", "Bad configuration", now.AddMinutes(3));
        Assert.Equal("Rollback completed", item.Resolution); Assert.Equal(actor, item.ResolvedByUserId);
        Assert.Throws<InvalidOperationException>(() => item.UpdateDetails("Rewrite", null, "API", IncidentSeverity.Low, now));
        Assert.Throws<InvalidOperationException>(() => item.SetOwner(Guid.NewGuid(), now));
        item.TransitionTo(IncidentStatus.Closed, actor, null, null, now.AddMinutes(4));
        Assert.Equal(now.AddMinutes(3), item.EndedAtUtc);
    }
    [Theory]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void Incident_SLA_uses_the_same_exact_completion_boundary_as_work_items(int minutes, bool breached)
    {
        var now = new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc);
        var policy = new SlaPolicy { Name = "Response", DurationMinutes = 60, WarningBeforeMinutes = 10 };
        var sla = IncidentSla.Start(Guid.NewGuid(), policy, now);
        Assert.True(sla.TryRecordWarning(now.AddMinutes(50))); Assert.False(sla.TryRecordWarning(now.AddMinutes(51)));
        Assert.Equal(breached, sla.Complete(now.AddMinutes(minutes)));
        Assert.Equal(breached, sla.BreachedAtUtc.HasValue); Assert.False(sla.TryRecordBreach(now.AddHours(3)));
    }
}
