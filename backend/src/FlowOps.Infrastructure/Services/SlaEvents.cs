using System.Text.Json;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;

namespace FlowOps.Infrastructure.Services;

internal static class SlaEvents
{
    public static async Task StageAsync(FlowOpsDbContext db, INotificationService notifications,
        WorkItemSla sla, string kind, DateTime now, CancellationToken cancellationToken)
    {
        var item = sla.WorkItem;
        var audit = new AuditEvent
        {
            OrganizationId = item.Project.OrganizationId, ActorUserId = null, Action = kind,
            EntityType = "WorkItem", EntityId = item.Id, OccurredAtUtc = now,
            DetailsJson = JsonSerializer.Serialize(new { slaId = sla.Id, sla.PolicyId, sla.DeadlineAtUtc, sla.BreachedAtUtc })
        };
        db.AuditEvents.Add(audit);
        var recipients = new[] { item.ReporterUserId }.Concat(item.AssigneeUserId is Guid assignee ? [assignee] : []);
        await notifications.QueueWorkItemAsync(null, audit.OrganizationId, item.ProjectId, item.Id,
            audit.Id, kind, item.Title, recipients, cancellationToken);
    }
}
