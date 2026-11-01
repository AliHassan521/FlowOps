namespace FlowOps.Application.Common.Interfaces;

/// <summary>Sends an invalidation hint, never the durable or tenant-sensitive notification payload.</summary>
public interface INotificationDispatcher
{
    Task NotifyChangedAsync(Guid userId, CancellationToken cancellationToken);
}
