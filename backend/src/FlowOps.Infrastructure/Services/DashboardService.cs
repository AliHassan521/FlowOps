using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Dashboard;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace FlowOps.Infrastructure.Services;

public sealed class DashboardService(FlowOpsDbContext db, TimeProvider clock) : IDashboardService
{
    public async Task<DashboardResponse> GetAsync(Guid actor, Guid organization, Guid? project, int periodDays, int workloadPage, CancellationToken ct)
    {
        if (periodDays is < 1 or > 90 || workloadPage < 1 || (long)(workloadPage - 1) * 20 > int.MaxValue) throw new ArgumentException("Period must be 1–90 days and workload page positive with a supported offset.");
        await TenantAccess.RequireAsync(db, actor, organization, project, ct);
        var now = clock.GetUtcNow().UtcDateTime; var cutoff = now.AddDays(-periodDays);
        var projects = db.Projects.Where(p => p.OrganizationId == organization && p.IsActive && p.Organization.IsActive);
        if (project.HasValue) projects = projects.Where(p => p.Id == project.Value);
        var ids = projects.Select(p => p.Id);
        var work = db.WorkItems.AsNoTracking().Where(w => ids.Contains(w.ProjectId));
        var incidents = db.Incidents.AsNoTracking().Where(i => ids.Contains(i.ProjectId));
        var releases = db.Releases.AsNoTracking().Where(r => ids.Contains(r.ProjectId));
        var workCounts = await work.GroupBy(w => 1).Select(g => new
        {
            Open = g.LongCount(w => w.Status != WorkItemStatus.Resolved && w.Status != WorkItemStatus.Closed),
            Critical = g.LongCount(w => w.Priority == WorkItemPriority.Critical && w.Status != WorkItemStatus.Resolved && w.Status != WorkItemStatus.Closed),
            Unassigned = g.LongCount(w => w.AssigneeUserId == null && w.Status != WorkItemStatus.Resolved && w.Status != WorkItemStatus.Closed),
            Resolved = g.LongCount(w => w.ResolvedAtUtc >= cutoff && w.ResolvedAtUtc <= now && (w.Status == WorkItemStatus.Resolved || w.Status == WorkItemStatus.Closed))
        }).SingleOrDefaultAsync(ct);
        var incidentCounts = await incidents.GroupBy(i => 1).Select(g => new
        {
            Active = g.LongCount(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed),
            Critical = g.LongCount(i => i.Severity == IncidentSeverity.Critical && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed),
            Resolved = g.LongCount(i => i.EndedAtUtc >= cutoff && i.EndedAtUtc <= now && (i.Status == IncidentStatus.Resolved || i.Status == IncidentStatus.Closed))
        }).SingleOrDefaultAsync(ct);
        var releaseCounts = await releases.Where(r => r.CompletedAtUtc >= cutoff && r.CompletedAtUtc <= now).GroupBy(r => 1).Select(g => new
        {
            Successful = g.LongCount(r => r.Status == ReleaseStatus.Successful),
            Failed = g.LongCount(r => r.Status == ReleaseStatus.Failed)
        }).SingleOrDefaultAsync(ct);
        var workSlas = db.WorkItemSlas.AsNoTracking().Where(s => ids.Contains(s.WorkItem.ProjectId)).Select(s => new { s.CompletedAtUtc, s.BreachedAtUtc, s.WarningAtUtc, s.DeadlineAtUtc });
        var incidentSlas = db.IncidentSlas.AsNoTracking().Where(s => ids.Contains(s.Incident.ProjectId)).Select(s => new { s.CompletedAtUtc, s.BreachedAtUtc, s.WarningAtUtc, s.DeadlineAtUtc });
        var slaCounts = await workSlas.Concat(incidentSlas).GroupBy(s => 1).Select(g => new
        {
            Risk = g.LongCount(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null && s.WarningAtUtc != null && s.WarningAtUtc <= now && s.DeadlineAtUtc > now),
            Breached = g.LongCount(s => s.BreachedAtUtc != null || s.CompletedAtUtc == null && s.DeadlineAtUtc <= now),
            Met = g.LongCount(s => s.CompletedAtUtc != null && s.BreachedAtUtc == null)
        }).SingleOrDefaultAsync(ct);
        var workload = db.OrganizationMemberships.AsNoTracking().Where(m => m.OrganizationId == organization && m.IsActive && m.User.IsActive && m.Role.Name != "Viewer")
            .Select(m => new
            {
                m.UserId,
                m.User.FullName,
                OpenWorkItems =
                work.LongCount(w => w.AssigneeUserId == m.UserId && w.Status != WorkItemStatus.Resolved && w.Status != WorkItemStatus.Closed),
                CriticalWorkItems = work.LongCount(w => w.AssigneeUserId == m.UserId && w.Priority == WorkItemPriority.Critical && w.Status != WorkItemStatus.Resolved && w.Status != WorkItemStatus.Closed),
                ActiveIncidents = incidents.LongCount(i => i.OwnerUserId == m.UserId && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
            })
            .Where(row => row.OpenWorkItems > 0 || row.ActiveIncidents > 0);
        var workloadCount = await workload.LongCountAsync(ct);
        var rows = await workload.OrderByDescending(row => row.OpenWorkItems + row.ActiveIncidents).ThenBy(row => row.UserId).Skip((workloadPage - 1) * 20).Take(20).Select(row => new TeamWorkloadResponse(row.UserId, row.FullName, row.OpenWorkItems, row.CriticalWorkItems, row.ActiveIncidents)).ToListAsync(ct);
        var recent = await releases.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Take(10).Select(ReleaseReadModel.Project).ToListAsync(ct);
        return new(now, periodDays, workCounts?.Open ?? 0, workCounts?.Critical ?? 0, incidentCounts?.Active ?? 0, incidentCounts?.Critical ?? 0,
            workCounts?.Unassigned ?? 0, slaCounts?.Risk ?? 0, slaCounts?.Breached ?? 0, slaCounts?.Met ?? 0, workCounts?.Resolved ?? 0, incidentCounts?.Resolved ?? 0,
            releaseCounts?.Successful ?? 0, releaseCounts?.Failed ?? 0, recent, rows, workloadPage, workloadCount);
    }
}
