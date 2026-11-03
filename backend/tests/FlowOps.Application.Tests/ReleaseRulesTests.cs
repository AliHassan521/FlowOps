using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using Xunit;
namespace FlowOps.Application.Tests;

public sealed class ReleaseRulesTests
{
    [Fact]
    public void Every_release_status_pair_has_an_explicit_allow_or_reject_rule()
    {
        foreach (var from in Enum.GetValues<ReleaseStatus>()) foreach (var to in Enum.GetValues<ReleaseStatus>())
        {
            var now = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc); var actor = Guid.NewGuid(); var r = new Release { CreatedAtUtc = now };
            if (from != ReleaseStatus.Draft) r.TransitionTo(ReleaseStatus.Approved, actor, null, now);
            if (from is ReleaseStatus.Deploying or ReleaseStatus.Successful or ReleaseStatus.Failed) r.TransitionTo(ReleaseStatus.Deploying, actor, null, now);
            if (from is ReleaseStatus.Successful or ReleaseStatus.Failed) r.TransitionTo(from, actor, "Recorded outcome", now);
            var allowed = (from, to) is (ReleaseStatus.Draft, ReleaseStatus.Approved) or (ReleaseStatus.Approved, ReleaseStatus.Deploying) or (ReleaseStatus.Deploying, ReleaseStatus.Successful) or (ReleaseStatus.Deploying, ReleaseStatus.Failed);
            if (allowed) r.TransitionTo(to, actor, to is ReleaseStatus.Successful or ReleaseStatus.Failed ? "Verified outcome" : null, now);
            else Assert.Throws<InvalidOperationException>(() => r.TransitionTo(to, actor, "Outcome", now));
        }
    }
    [Theory]
    [InlineData("Successful")]
    [InlineData("Failed")]
    public void Approved_details_freeze_and_completion_requires_an_outcome_with_monotonic_timestamps(string outcome)
    {
        var now = new DateTime(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc); var actor = Guid.NewGuid(); var r = new Release { CreatedAtUtc = now }; r.UpdateNotes("Draft notes", now);
        r.TransitionTo(ReleaseStatus.Approved, actor, null, now.AddMinutes(1)); Assert.Equal(actor, r.ApprovedByUserId);
        Assert.Throws<InvalidOperationException>(() => r.UpdateNotes("Rewrite approved notes", now));
        Assert.Throws<ArgumentException>(() => r.TransitionTo(ReleaseStatus.Deploying, actor, null, now));
        r.TransitionTo(ReleaseStatus.Deploying, actor, null, now.AddMinutes(2));
        var status = Enum.Parse<ReleaseStatus>(outcome); Assert.Throws<ArgumentException>(() => r.TransitionTo(status, actor, " ", now.AddMinutes(3)));
        r.TransitionTo(status, actor, "  Verified result  ", now.AddMinutes(3)); Assert.Equal("Verified result", r.Outcome); Assert.Equal(now.AddMinutes(3), r.CompletedAtUtc);
        Assert.Throws<InvalidOperationException>(() => r.TransitionTo(ReleaseStatus.Draft, actor, null, now.AddMinutes(4)));
    }
    [Fact]
    public void Work_item_resolution_date_survives_closure_and_metadata_changes_but_clears_on_reopen()
    {
        var now = DateTime.UtcNow; var w = new WorkItem { AssigneeUserId = Guid.NewGuid() };
        foreach (var s in new[] { WorkItemStatus.Triaged, WorkItemStatus.Assigned, WorkItemStatus.InProgress, WorkItemStatus.CodeReview, WorkItemStatus.QA, WorkItemStatus.Resolved }) w.TransitionTo(s, now);
        w.TransitionTo(WorkItemStatus.Closed, now.AddDays(1)); w.UpdateDetails("New title", null, WorkItemPriority.High, null, now.AddDays(2)); Assert.Equal(now, w.ResolvedAtUtc);
        w.TransitionTo(WorkItemStatus.InProgress, now.AddDays(3)); Assert.Null(w.ResolvedAtUtc);
    }
}
