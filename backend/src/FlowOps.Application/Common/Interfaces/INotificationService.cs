using FlowOps.Contracts.Common;
using FlowOps.Contracts.Notifications;

namespace FlowOps.Application.Common.Interfaces;

public interface INotificationService
{
    Task QueueWorkItemAsync(Guid? actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        Guid eventId, string kind, string title, IEnumerable<Guid> recipientIds, CancellationToken cancellationToken);
    Task QueueIncidentAsync(Guid? actorUserId, Guid organizationId, Guid projectId, Guid incidentId,
        Guid eventId, string kind, string title, IEnumerable<Guid> recipientIds, CancellationToken cancellationToken);
    Task QueueReleaseAsync(Guid? actorUserId, Guid organizationId, Guid projectId, Guid releaseId,
        Guid eventId, string title, IEnumerable<Guid> recipientIds, CancellationToken cancellationToken);
    Task PublishPendingAsync();
    Task<PagedResponse<NotificationResponse>> ListAsync(Guid userId, int page, int pageSize, bool unreadOnly, CancellationToken cancellationToken);
    Task<NotificationSummaryResponse> SummaryAsync(Guid userId, CancellationToken cancellationToken);
    Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken);
    Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken);
}
