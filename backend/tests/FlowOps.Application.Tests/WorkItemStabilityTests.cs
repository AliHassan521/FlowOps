using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.WorkItems;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using FlowOps.Infrastructure.Persistence.Configurations;
using FlowOps.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FlowOps.Application.Tests;

public sealed class WorkItemStabilityTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<FlowOpsDbContext> options;
    private readonly User actor = new() { Email = "actor@example.com", FullName = "Actor", PasswordHash = "test-only" };
    private readonly User assignee = new() { Email = "assignee@example.com", FullName = "Assignee", PasswordHash = "test-only" };
    private readonly Organization organization = new() { Name = "Team", Slug = "team" };
    private readonly Project project;
    private readonly WorkItem item;
    private readonly RecordingDispatcher dispatcher = new();

    public WorkItemStabilityTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<FlowOpsDbContext>().UseSqlite(connection).Options;
        using var db = new FlowOpsDbContext(options);
        db.Database.EnsureCreated();
        project = new Project { Organization = organization, Name = "Delivery", Key = "DEL" };
        item = new WorkItem { Project = project, Reporter = actor, Assignee = assignee, AssigneeUserId = assignee.Id, Title = "Ship safely" };
        foreach (var status in new[] { WorkItemStatus.Triaged, WorkItemStatus.Assigned, WorkItemStatus.InProgress, WorkItemStatus.CodeReview })
            item.TransitionTo(status, DateTime.UtcNow);
        db.AddRange(actor, assignee, organization, project, item,
            new OrganizationMembership { Organization = organization, User = actor, RoleId = RoleConfiguration.AdminRoleId },
            new OrganizationMembership { Organization = organization, User = assignee, RoleId = RoleConfiguration.EngineerRoleId });
        db.SaveChanges();
    }

    [Fact]
    public async Task Stale_transition_rolls_back_business_audit_and_notifications()
    {
        await using var first = new FlowOpsDbContext(options);
        await using var stale = new FlowOpsDbContext(options);
        await first.WorkItems.SingleAsync(x => x.Id == item.Id);
        await stale.WorkItems.SingleAsync(x => x.Id == item.Id);
        await Service(first).TransitionAsync(actor.Id, organization.Id, project.Id, item.Id, new TransitionWorkItemRequest("InProgress"));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Service(stale)
            .TransitionAsync(actor.Id, organization.Id, project.Id, item.Id, new TransitionWorkItemRequest("QA")));
        await using var verify = new FlowOpsDbContext(options);
        Assert.Equal(WorkItemStatus.InProgress, (await verify.WorkItems.SingleAsync()).Status);
        Assert.Single(await verify.AuditEvents.ToListAsync());
        Assert.Single(await verify.Notifications.ToListAsync());
        Assert.Single(dispatcher.Recipients);
    }

    [Theory]
    [InlineData(WorkItemStatus.Assigned)]
    [InlineData(WorkItemStatus.InProgress)]
    public void Required_assignee_cannot_be_removed(WorkItemStatus status)
    {
        var workItem = new WorkItem { AssigneeUserId = assignee.Id };
        workItem.TransitionTo(WorkItemStatus.Triaged, DateTime.UtcNow);
        workItem.TransitionTo(WorkItemStatus.Assigned, DateTime.UtcNow);
        if (status == WorkItemStatus.InProgress) workItem.TransitionTo(status, DateTime.UtcNow);
        Assert.Throws<InvalidOperationException>(() => workItem.SetAssignee(null, DateTime.UtcNow));
        Assert.Equal(assignee.Id, workItem.AssigneeUserId);
    }

    [Fact]
    public async Task Failed_realtime_hint_does_not_lose_a_committed_notification()
    {
        dispatcher.Fail = true;
        await using var db = new FlowOpsDbContext(options);
        var response = await Service(db).TransitionAsync(actor.Id, organization.Id, project.Id, item.Id, new TransitionWorkItemRequest("InProgress"));
        Assert.Equal("InProgress", response.Status);
        var notification = await db.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(assignee.Id, notification.UserId);
        Assert.Equal(DateTimeKind.Utc, notification.CreatedAtUtc.Kind);
        Assert.Equal(DateTimeKind.Utc, (await db.WorkItems.AsNoTracking().SingleAsync()).CreatedAtUtc.Kind);
    }

    [Fact]
    public async Task Recipients_are_distinct_active_and_never_include_the_actor()
    {
        await using var db = new FlowOpsDbContext(options);
        var service = new NotificationService(db, dispatcher, NullLogger<NotificationService>.Instance);
        var eventId = Guid.NewGuid();
        var recipients = new[] { actor.Id, assignee.Id, assignee.Id, Guid.NewGuid() };
        await service.QueueWorkItemAsync(actor.Id, organization.Id, project.Id, item.Id, eventId,
            "WorkItem.CommentAdded", item.Title, recipients, default);
        await service.QueueWorkItemAsync(actor.Id, organization.Id, project.Id, item.Id, eventId,
            "WorkItem.CommentAdded", item.Title, recipients, default);
        await db.SaveChangesAsync();
        Assert.Equal(assignee.Id, (await db.Notifications.SingleAsync()).UserId);
    }

    private WorkItemService Service(FlowOpsDbContext db) => new(db,
        new NotificationService(db, dispatcher, NullLogger<NotificationService>.Instance));

    public void Dispose() => connection.Dispose();

    private sealed class RecordingDispatcher : INotificationDispatcher
    {
        public List<Guid> Recipients { get; } = [];
        public bool Fail { get; set; }
        public Task NotifyChangedAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (Fail) throw new IOException("Simulated transport failure.");
            Recipients.Add(userId);
            return Task.CompletedTask;
        }
    }
}
