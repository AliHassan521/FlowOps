using FlowOps.Application.Common.Exceptions;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace FlowOps.Infrastructure.Services;

internal static class TenantAccess
{
    public static async Task<string> RequireAsync(FlowOpsDbContext db, Guid actor, Guid organization, Guid? project, CancellationToken ct)
    {
        var role = await db.OrganizationMemberships.Where(m => m.UserId == actor && m.OrganizationId == organization && m.IsActive && m.User.IsActive && m.Organization.IsActive)
            .Select(m => m.Role.Name).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("The organization was not found.");
        if (project.HasValue && !await db.Projects.AnyAsync(p => p.Id == project && p.OrganizationId == organization && p.IsActive && p.Organization.IsActive, ct))
            throw new KeyNotFoundException("The project was not found.");
        return role;
    }
    public static bool IsLead(string role) => role is "Admin" or "TeamLead";
    public static void Lead(string role) { if (!IsLead(role)) throw new ForbiddenException("An organization admin or team lead must perform this action."); }
    public static void Write(string role) { if (role == "Viewer") throw new ForbiddenException("Your organization role is read-only."); }
}
