using System.Text.Json;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace FlowOps.Infrastructure.Services;

internal static class IncidentSlaEvents
{
    public static async Task<bool> ProcessAsync(FlowOpsDbContext db, INotificationService notifications, TimeProvider clock, Guid id, CancellationToken ct)
    {
        var sla = await db.IncidentSlas.Include(s => s.Incident).ThenInclude(i => i.Project).ThenInclude(p => p.Organization).SingleOrDefaultAsync(s => s.Id == id, ct);
        if (sla is null || sla.CompletedAtUtc.HasValue || sla.BreachedAtUtc.HasValue || !sla.Incident.Project.IsActive || !sla.Incident.Project.Organization.IsActive
            || sla.Incident.Status is IncidentStatus.Resolved or IncidentStatus.Closed) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var kind = sla.TryRecordBreach(now) ? "Incident.SlaBreached" : sla.TryRecordWarning(now) ? "Incident.SlaWarning" : null;
        if (kind is null) return false;
        db.Entry(sla.Incident).Property(i => i.Version).IsModified = true;
        await StageAsync(db, notifications, sla, kind, now, ct);
        await db.SaveChangesAsync(ct); await notifications.PublishPendingAsync(); return true;
    }
    public static async Task StageAsync(FlowOpsDbContext db, INotificationService notifications, IncidentSla sla, string kind, DateTime now, CancellationToken ct)
    {
        var item = sla.Incident;
        var audit = new AuditEvent
        {
            OrganizationId = item.Project.OrganizationId,
            ActorUserId = null,
            Action = kind,
            EntityType = "Incident",
            EntityId = item.Id,
            OccurredAtUtc = now,
            DetailsJson = JsonSerializer.Serialize(new { slaId = sla.Id, sla.PolicyId, sla.DeadlineAtUtc, sla.BreachedAtUtc })
        };
        db.AuditEvents.Add(audit);
        var recipients = new[] { item.ReporterUserId }.Concat(item.OwnerUserId is Guid owner ? [owner] : []);
        await notifications.QueueIncidentAsync(null, audit.OrganizationId, item.ProjectId, item.Id, audit.Id, kind, item.Title, recipients, ct);
    }
}
