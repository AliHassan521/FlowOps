using FlowOps.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FlowOps.Api.Hubs;

public sealed class SignalRNotificationDispatcher(IHubContext<NotificationsHub> hub) : INotificationDispatcher
{
    public Task NotifyChangedAsync(Guid userId, CancellationToken cancellationToken) =>
        hub.Clients.User(userId.ToString()).SendAsync("NotificationsChanged", cancellationToken);
}
