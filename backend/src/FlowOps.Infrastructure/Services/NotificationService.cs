using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Notifications;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlowOps.Infrastructure.Services;

public sealed class NotificationService(FlowOpsDbContext db, INotificationDispatcher dispatcher,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly HashSet<Guid> pendingUsers = [];

    public async Task QueueWorkItemAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        Guid eventId, string kind, string title, IEnumerable<Guid> recipientIds, CancellationToken cancellationToken)
    {
        var candidates = recipientIds.Where(id => id != actorUserId).Distinct().ToArray();
        if (candidates.Length == 0) return;
        var recipients = await db.OrganizationMemberships.Where(m => m.OrganizationId == organizationId
            && candidates.Contains(m.UserId) && m.IsActive && m.User.IsActive && m.Organization.IsActive)
            .Select(m => m.UserId).ToListAsync(cancellationToken);
        var action = kind switch
        {
            "WorkItem.Assigned" => "You were assigned",
            "WorkItem.StatusChanged" => "Status changed",
            "WorkItem.CommentAdded" => "New comment",
            _ => throw new ArgumentException("Unsupported notification event.")
        };
        foreach (var recipient in recipients)
        {
            if (db.Notifications.Local.Any(n => n.EventId == eventId && n.UserId == recipient)) continue;
            db.Notifications.Add(new Notification
            {
                UserId = recipient, OrganizationId = organizationId, ProjectId = projectId,
                WorkItemId = workItemId, EventId = eventId, Kind = kind, Message = $"{action}: {title}"
            });
            pendingUsers.Add(recipient);
        }
    }

    public async Task PublishPendingAsync()
    {
        // Database commit already succeeded. A failed hint must not turn it into an apparent failed write.
        foreach (var userId in pendingUsers.ToArray()) await PublishSafelyAsync(userId);
        pendingUsers.Clear();
    }

    public async Task<PagedResponse<NotificationResponse>> ListAsync(Guid userId, int page, int pageSize,
        bool unreadOnly, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be positive and pageSize must be between 1 and 100.");
        var query = VisibleTo(userId);
        if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);
        var count = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderByDescending(n => n.CreatedAtUtc).ThenByDescending(n => n.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(n => new NotificationResponse(n.Id, n.OrganizationId, n.ProjectId, n.WorkItemId,
                n.Kind, n.Message, n.CreatedAtUtc, n.ReadAtUtc)).ToListAsync(cancellationToken);
        return new PagedResponse<NotificationResponse>(items, page, pageSize, count);
    }

    public async Task<NotificationSummaryResponse> SummaryAsync(Guid userId, CancellationToken cancellationToken) =>
        new(await VisibleTo(userId).LongCountAsync(n => n.ReadAtUtc == null, cancellationToken));

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        if (!await VisibleTo(userId).AnyAsync(n => n.Id == notificationId, cancellationToken))
            throw new KeyNotFoundException("The notification was not found.");
        var visibleIds = VisibleTo(userId).Select(n => n.Id);
        await db.Notifications.Where(n => visibleIds.Contains(n.Id) && n.Id == notificationId && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAtUtc, DateTime.UtcNow), cancellationToken);
        await PublishSafelyAsync(userId);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var visibleIds = VisibleTo(userId).Select(n => n.Id);
        await db.Notifications.Where(n => visibleIds.Contains(n.Id) && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.ReadAtUtc, DateTime.UtcNow), cancellationToken);
        await PublishSafelyAsync(userId);
    }

    private IQueryable<Notification> VisibleTo(Guid userId) => db.Notifications.AsNoTracking().Where(n =>
        n.UserId == userId && n.User.IsActive && n.Organization.IsActive && n.Project.IsActive
        && db.OrganizationMemberships.Any(m => m.OrganizationId == n.OrganizationId && m.UserId == userId && m.IsActive));

    private async Task PublishSafelyAsync(Guid userId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await dispatcher.NotifyChangedAsync(userId, timeout.Token);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Notification hint failed for user {UserId}; persisted inbox remains available.", userId);
        }
    }
}
