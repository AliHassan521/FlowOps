using FlowOps.Application.Common.Interfaces;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Services;

public sealed class SlaProcessor(FlowOpsDbContext db, INotificationService notifications, TimeProvider clock) : ISlaProcessor
{
    public async Task<IReadOnlyList<Guid>> GetDueIdsAsync(int batchSize, CancellationToken cancellationToken, IReadOnlyCollection<Guid>? excludedIds = null)
    {
        if (batchSize is < 1 or > 500) throw new ArgumentException("SLA batch size must be 1–500.");
        var now = clock.GetUtcNow().UtcDateTime;
        var excluded = excludedIds?.ToArray() ?? [];
        var workItems = db.WorkItemSlas.AsNoTracking().Where(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null
            && !excluded.Contains(s.Id) && s.WorkItem.Project.IsActive && s.WorkItem.Project.Organization.IsActive
            && s.WorkItem.Status != WorkItemStatus.Resolved && s.WorkItem.Status != WorkItemStatus.Closed
            && (s.DeadlineAtUtc <= now || (s.WarningAtUtc != null && s.WarningAtUtc <= now && s.WarnedAtUtc == null)))
            .Select(s => new { s.Id, s.DeadlineAtUtc });
        var incidents = db.IncidentSlas.AsNoTracking().Where(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null
            && !excluded.Contains(s.Id) && s.Incident.Project.IsActive && s.Incident.Project.Organization.IsActive
            && s.Incident.Status != IncidentStatus.Resolved && s.Incident.Status != IncidentStatus.Closed
            && (s.DeadlineAtUtc <= now || (s.WarningAtUtc != null && s.WarningAtUtc <= now && s.WarnedAtUtc == null)))
            .Select(s => new { s.Id, s.DeadlineAtUtc });
        return await workItems.Concat(incidents).OrderBy(s => s.DeadlineAtUtc).ThenBy(s => s.Id).Take(batchSize)
            .Select(s => s.Id).ToListAsync(cancellationToken);
    }

    public async Task<bool> ProcessOneAsync(Guid slaId, CancellationToken cancellationToken)
    {
        var sla = await db.WorkItemSlas.Include(s => s.WorkItem).ThenInclude(w => w.Project).ThenInclude(p => p.Organization)
            .SingleOrDefaultAsync(s => s.Id == slaId, cancellationToken);
        if (sla is null) return await IncidentSlaEvents.ProcessAsync(db, notifications, clock, slaId, cancellationToken);
        if (sla.CompletedAtUtc.HasValue || sla.BreachedAtUtc.HasValue
            || !sla.WorkItem.Project.IsActive || !sla.WorkItem.Project.Organization.IsActive
            || sla.WorkItem.Status is WorkItemStatus.Resolved or WorkItemStatus.Closed) return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var kind = sla.TryRecordBreach(now) ? "Sla.Breached" : sla.TryRecordWarning(now) ? "Sla.Warning" : null;
        if (kind is null) return false;
        // A resolver/reassigner that changes the parent concurrently must conflict with this measurement.
        db.Entry(sla.WorkItem).Property(w => w.Version).IsModified = true;
        await SlaEvents.StageAsync(db, notifications, sla, kind, now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await notifications.PublishPendingAsync();
        return true;
    }
}
