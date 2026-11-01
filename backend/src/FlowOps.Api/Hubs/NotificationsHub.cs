using FlowOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Api.Hubs;

[Authorize]
public sealed class NotificationsHub(FlowOpsDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (!Guid.TryParse(Context.UserIdentifier, out var userId)
            || !await db.Users.AnyAsync(u => u.Id == userId && u.IsActive, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }
        await base.OnConnectedAsync();
    }
    // No caller-controlled groups or data methods. Hints go only to the authenticated user.
}
