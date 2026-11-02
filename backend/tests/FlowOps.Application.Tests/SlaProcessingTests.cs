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

public sealed class SlaProcessingTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<FlowOpsDbContext> options;
    private readonly ManualTimeProvider clock = new(new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.Zero));
    private readonly User actor = new() { Email = "sla-admin@example.test", FullName = "Lead", PasswordHash = "test-only" };
    private readonly User engineer = new() { Email = "sla-engineer@example.test", FullName = "Engineer", PasswordHash = "test-only" };
    private readonly Organization organization = new() { Name = "SLA team", Slug = "sla-team" };
    private readonly WorkItem item;
    private readonly WorkItemSla sla;
    private readonly Dispatcher dispatcher = new();

    public SlaProcessingTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<FlowOpsDbContext>().UseSqlite(connection).Options;
        using var db = Context(); db.Database.EnsureCreated();
        var project = new Project { Organization = organization, Name = "Delivery", Key = "DEL" };
        item = new WorkItem { Project = project, Reporter = actor, Assignee = engineer, AssigneeUserId = engineer.Id, Title = "SLA work" };
        foreach (var status in new[] { WorkItemStatus.Triaged, WorkItemStatus.Assigned, WorkItemStatus.InProgress, WorkItemStatus.CodeReview, WorkItemStatus.QA })
            item.TransitionTo(status, clock.GetUtcNow().UtcDateTime);
        var policy = new SlaPolicy { Organization = organization, Name = "Two hours", NormalizedName = "TWO HOURS", DurationMinutes = 120, WarningBeforeMinutes = 30 };
        sla = WorkItemSla.Start(item.Id, policy, clock.GetUtcNow().UtcDateTime);
        db.AddRange(actor, engineer, organization, project, item, policy, sla,
            new OrganizationMembership { Organization = organization, User = actor, RoleId = RoleConfiguration.AdminRoleId },
            new OrganizationMembership { Organization = organization, User = engineer, RoleId = RoleConfiguration.EngineerRoleId });
        db.SaveChanges();
    }

    [Fact]
    public async Task Warning_and_breach_persist_exactly_once_across_new_processor_scopes()
    {
        clock.Advance(TimeSpan.FromMinutes(90));
        await using (var db = Context()) Assert.True(await Processor(db).ProcessOneAsync(sla.Id, default));
        await using (var restarted = Context()) Assert.False(await Processor(restarted).ProcessOneAsync(sla.Id, default));
        clock.Advance(TimeSpan.FromMinutes(30));
        await using (var db = Context()) Assert.True(await Processor(db).ProcessOneAsync(sla.Id, default));
        await using (var restarted = Context()) Assert.False(await Processor(restarted).ProcessOneAsync(sla.Id, default));
        await using var verify = Context();
        var audits = await verify.AuditEvents.OrderBy(a => a.OccurredAtUtc).ToListAsync();
        Assert.Equal(new[] { "Sla.Warning", "Sla.Breached" }, audits.Select(a => a.Action));
        Assert.All(audits, a => Assert.Null(a.ActorUserId));
        Assert.Equal(4, await verify.Notifications.CountAsync());
        Assert.Equal(sla.DeadlineAtUtc, (await verify.WorkItemSlas.SingleAsync()).BreachedAtUtc);
        Assert.Equal(4, dispatcher.Users.Count);
    }

    [Fact]
    public async Task Overlapping_processors_cannot_duplicate_events_or_notifications()
    {
        clock.Advance(TimeSpan.FromHours(3));
        await using var first = Context(); await using var stale = Context();
        await stale.WorkItemSlas.Include(s => s.WorkItem).ThenInclude(w => w.Project).ThenInclude(p => p.Organization).SingleAsync();
        Assert.True(await Processor(first).ProcessOneAsync(sla.Id, default));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Processor(stale).ProcessOneAsync(sla.Id, default));
        await using var verify = Context();
        Assert.Single(await verify.AuditEvents.ToListAsync());
        Assert.Equal(2, await verify.Notifications.CountAsync());
        Assert.Null((await verify.WorkItemSlas.SingleAsync()).WarnedAtUtc);
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(130, true)]
    public async Task Resolution_stops_the_measurement_and_records_a_late_breach_even_without_a_worker_tick(int minutes, bool breached)
    {
        clock.Advance(TimeSpan.FromMinutes(minutes));
        await using (var db = Context())
        {
            var notifications = Notifications(db);
            var workItems = new WorkItemService(db, notifications, new SlaService(db, notifications, clock));
            await workItems.TransitionAsync(actor.Id, organization.Id, item.ProjectId, item.Id, new TransitionWorkItemRequest("Resolved"));
        }
        clock.Advance(TimeSpan.FromHours(4));
        await using var verify = Context();
        Assert.False(await Processor(verify).ProcessOneAsync(sla.Id, default));
        var measurement = await verify.WorkItemSlas.SingleAsync();
        Assert.NotNull(measurement.CompletedAtUtc);
        Assert.Equal(breached, measurement.BreachedAtUtc.HasValue);
        Assert.Equal(breached ? 1 : 0, await verify.AuditEvents.CountAsync(a => a.Action == "Sla.Breached"));
        Assert.Equal(breached ? 3 : 1, await verify.Notifications.CountAsync());
    }

    [Fact]
    public async Task A_concurrent_resolution_rolls_back_a_stale_warning()
    {
        clock.Advance(TimeSpan.FromMinutes(100));
        await using var stale = Context();
        await stale.WorkItemSlas.Include(s => s.WorkItem).ThenInclude(w => w.Project).ThenInclude(p => p.Organization).SingleAsync();
        await using (var resolver = Context())
        {
            var notifications = Notifications(resolver);
            await new WorkItemService(resolver, notifications, new SlaService(resolver, notifications, clock))
                .TransitionAsync(actor.Id, organization.Id, item.ProjectId, item.Id, new TransitionWorkItemRequest("Resolved"));
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Processor(stale).ProcessOneAsync(sla.Id, default));
        await using var verify = Context();
        Assert.Null((await verify.WorkItemSlas.SingleAsync()).WarnedAtUtc);
        Assert.False(await verify.AuditEvents.AnyAsync(a => a.Action == "Sla.Warning"));
    }

    [Fact]
    public async Task Archived_projects_are_excluded_and_batches_are_bounded()
    {
        clock.Advance(TimeSpan.FromHours(3));
        await using var db = Context();
        Assert.Single(await Processor(db).GetDueIdsAsync(1, default));
        (await db.Projects.SingleAsync()).IsActive = false; await db.SaveChangesAsync();
        Assert.Empty(await Processor(db).GetDueIdsAsync(1, default));
        Assert.False(await Processor(db).ProcessOneAsync(sla.Id, default));
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task Inactive_recipients_are_skipped_without_losing_breach_history()
    {
        clock.Advance(TimeSpan.FromHours(3));
        await using var db = Context();
        foreach (var member in await db.OrganizationMemberships.ToListAsync()) member.IsActive = false;
        await db.SaveChangesAsync();
        Assert.True(await Processor(db).ProcessOneAsync(sla.Id, default));
        Assert.Empty(await db.Notifications.ToListAsync());
        Assert.Single(await db.AuditEvents.ToListAsync());
    }

    [Fact]
    public async Task Cancellation_does_not_record_partial_processing()
    {
        clock.Advance(TimeSpan.FromHours(3));
        await using var db = Context();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Processor(db).ProcessOneAsync(sla.Id, cancelled.Token));
        await using var verify = Context();
        Assert.Empty(await verify.AuditEvents.ToListAsync());
        Assert.Null((await verify.WorkItemSlas.SingleAsync()).BreachedAtUtc);
    }

    private FlowOpsDbContext Context() => new(options);
    private NotificationService Notifications(FlowOpsDbContext db) => new(db, dispatcher, NullLogger<NotificationService>.Instance, clock);
    private SlaProcessor Processor(FlowOpsDbContext db) => new(db, Notifications(db), clock);
    public void Dispose() => connection.Dispose();
    private sealed class Dispatcher : INotificationDispatcher
    {
        public List<Guid> Users { get; } = [];
        public Task NotifyChangedAsync(Guid userId, CancellationToken cancellationToken) { Users.Add(userId); return Task.CompletedTask; }
    }
}
