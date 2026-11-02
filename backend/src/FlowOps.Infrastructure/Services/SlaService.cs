using System.Text.Json;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Sla;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Domain.Sla;
using FlowOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Services;

public sealed class SlaService(FlowOpsDbContext db, INotificationService notifications, TimeProvider clock) : ISlaService
{
    public async Task<SlaPolicyResponse> CreatePolicyAsync(Guid actorId, Guid organizationId,
        CreateSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actorId, organizationId, true, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 2 or > 120)
            throw new ArgumentException("Policy name must be 2–120 characters.");
        var now = clock.GetUtcNow().UtcDateTime;
        SlaSchedule.Calculate(now, request.DurationMinutes, request.WarningBeforeMinutes);
        var normalizedName = request.Name.Trim().ToUpperInvariant();
        if (await db.SlaPolicies.AnyAsync(p => p.OrganizationId == organizationId && p.NormalizedName == normalizedName, cancellationToken))
            throw new ConflictException("A policy with this name already exists in the organization.");
        var policy = new SlaPolicy { OrganizationId = organizationId, Name = request.Name.Trim(), NormalizedName = normalizedName,
            DurationMinutes = request.DurationMinutes, WarningBeforeMinutes = request.WarningBeforeMinutes, CreatedAtUtc = now };
        db.SlaPolicies.Add(policy);
        AddAudit(actorId, organizationId, "SlaPolicy.Created", "SlaPolicy", policy.Id,
            JsonSerializer.Serialize(new { policy.Name, policy.DurationMinutes, policy.WarningBeforeMinutes }));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        { throw new ConflictException("A policy with this name already exists in the organization."); }
        return MapPolicy(policy);
    }

    public async Task<PagedResponse<SlaPolicyResponse>> ListPoliciesAsync(Guid actorId, Guid organizationId,
        int page, int pageSize, bool activeOnly, CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100 || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException("Page must be positive and pageSize must be 1–100.");
        await EnsureAccessAsync(actorId, organizationId, false, cancellationToken);
        var query = db.SlaPolicies.AsNoTracking().Where(p => p.OrganizationId == organizationId && (!activeOnly || p.IsActive));
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(p => p.Name).ThenBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new SlaPolicyResponse(p.Id, p.Name, p.DurationMinutes, p.WarningBeforeMinutes, p.IsActive, p.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new(items, page, pageSize, total);
    }

    public async Task ArchivePolicyAsync(Guid actorId, Guid organizationId, Guid policyId, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actorId, organizationId, true, cancellationToken);
        var policy = await db.SlaPolicies.SingleOrDefaultAsync(p => p.Id == policyId && p.OrganizationId == organizationId, cancellationToken)
            ?? throw new KeyNotFoundException("The SLA policy was not found.");
        if (!policy.IsActive) return;
        policy.IsActive = false;
        AddAudit(actorId, organizationId, "SlaPolicy.Archived", "SlaPolicy", policyId, null);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkItemSlaStateResponse> GetWorkItemAsync(Guid actorId, Guid organizationId,
        Guid projectId, Guid workItemId, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actorId, organizationId, false, cancellationToken);
        await FindItemAsync(organizationId, projectId, workItemId, cancellationToken);
        var sla = await db.WorkItemSlas.AsNoTracking().SingleOrDefaultAsync(s => s.WorkItemId == workItemId, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        return new(sla is null ? null : MapSla(sla, now), now);
    }

    public async Task<WorkItemSlaStateResponse> AttachAsync(Guid actorId, Guid organizationId,
        Guid projectId, Guid workItemId, Guid policyId, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actorId, organizationId, true, cancellationToken);
        var item = await FindItemAsync(organizationId, projectId, workItemId, cancellationToken);
        if (item.Status is WorkItemStatus.Resolved or WorkItemStatus.Closed)
            throw new ConflictException("An SLA cannot be started on a resolved or closed work item.");
        if (await db.WorkItemSlas.AnyAsync(s => s.WorkItemId == workItemId, cancellationToken))
            throw new ConflictException("This work item already has an SLA. Its measurement cannot be reset.");
        var policy = await db.SlaPolicies.SingleOrDefaultAsync(p => p.Id == policyId && p.OrganizationId == organizationId && p.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("An active SLA policy was not found.");
        var now = clock.GetUtcNow().UtcDateTime;
        var sla = WorkItemSla.Start(workItemId, policy, now);
        db.WorkItemSlas.Add(sla);
        // Coordinate attachment with simultaneous workflow changes through the existing work-item token.
        db.Entry(item).Property(w => w.Version).IsModified = true;
        AddAudit(actorId, organizationId, "WorkItem.SlaAttached", "WorkItem", workItemId,
            JsonSerializer.Serialize(new { sla.Id, sla.PolicyId, sla.PolicyName, sla.DeadlineAtUtc, sla.WarningAtUtc }));
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        { throw new ConflictException("This work item already has an SLA."); }
        return new(MapSla(sla, now), now);
    }

    public async Task<SlaSummaryResponse> SummaryAsync(Guid actorId, Guid organizationId, CancellationToken cancellationToken)
    {
        await EnsureAccessAsync(actorId, organizationId, false, cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        return await db.WorkItemSlas.AsNoTracking().Where(s => s.WorkItem.Project.OrganizationId == organizationId
                && s.WorkItem.Project.IsActive && s.WorkItem.Project.Organization.IsActive)
            .GroupBy(s => 1).Select(group => new SlaSummaryResponse(
                group.LongCount(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null && s.DeadlineAtUtc > now),
                group.LongCount(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null && s.WarningAtUtc != null && s.WarningAtUtc <= now && s.DeadlineAtUtc > now),
                group.LongCount(s => s.BreachedAtUtc != null || (s.CompletedAtUtc == null && s.DeadlineAtUtc <= now)),
                group.LongCount(s => s.CompletedAtUtc != null && s.BreachedAtUtc == null)))
            .SingleOrDefaultAsync(cancellationToken) ?? new(0, 0, 0, 0);
    }

    public async Task CompleteForResolutionAsync(WorkItem item, Guid organizationId, CancellationToken cancellationToken)
    {
        var sla = await db.WorkItemSlas.Include(s => s.WorkItem).ThenInclude(w => w.Project)
            .SingleOrDefaultAsync(s => s.WorkItemId == item.Id, cancellationToken);
        if (sla is null || sla.CompletedAtUtc.HasValue) return;
        var now = clock.GetUtcNow().UtcDateTime;
        if (sla.Complete(now)) await SlaEvents.StageAsync(db, notifications, sla, "Sla.Breached", now, cancellationToken);
        AddAudit(null, organizationId, "Sla.Completed", "WorkItem", item.Id,
            JsonSerializer.Serialize(new { sla.Id, sla.CompletedAtUtc, sla.BreachedAtUtc }));
    }

    private async Task EnsureAccessAsync(Guid actorId, Guid organizationId, bool requireLead, CancellationToken cancellationToken)
    {
        var membership = await db.OrganizationMemberships.Include(m => m.Role).SingleOrDefaultAsync(m =>
            m.UserId == actorId && m.OrganizationId == organizationId && m.IsActive && m.User.IsActive && m.Organization.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The organization was not found.");
        if (requireLead && membership.Role.Name is not ("Admin" or "TeamLead"))
            throw new ForbiddenException("An organization administrator or team lead must manage SLA policies and measurements.");
    }

    private async Task<WorkItem> FindItemAsync(Guid organizationId, Guid projectId, Guid itemId, CancellationToken cancellationToken) =>
        await db.WorkItems.Include(w => w.Project).SingleOrDefaultAsync(w => w.Id == itemId && w.ProjectId == projectId
            && w.Project.OrganizationId == organizationId && w.Project.IsActive && w.Project.Organization.IsActive, cancellationToken)
        ?? throw new KeyNotFoundException("The work item was not found.");

    private void AddAudit(Guid? actorId, Guid orgId, string action, string type, Guid entityId, string? details) =>
        db.AuditEvents.Add(new AuditEvent { ActorUserId = actorId, OrganizationId = orgId, Action = action,
            EntityType = type, EntityId = entityId, DetailsJson = details, OccurredAtUtc = clock.GetUtcNow().UtcDateTime });

    private static SlaPolicyResponse MapPolicy(SlaPolicy p) => new(p.Id, p.Name, p.DurationMinutes, p.WarningBeforeMinutes, p.IsActive, p.CreatedAtUtc);

    private static WorkItemSlaResponse MapSla(WorkItemSla s, DateTime now)
    {
        var status = s.BreachedAtUtc.HasValue ? "Breached" : s.CompletedAtUtc.HasValue ? "Met"
            : now >= s.DeadlineAtUtc ? "Overdue" : s.WarningAtUtc.HasValue && now >= s.WarningAtUtc ? "AtRisk" : "Active";
        return new(s.Id, s.PolicyId, s.PolicyName, s.DurationMinutes, s.WarningBeforeMinutes, s.StartedAtUtc,
            s.DeadlineAtUtc, s.WarningAtUtc, s.WarnedAtUtc, s.BreachedAtUtc, s.CompletedAtUtc, status,
            (long)Math.Ceiling((s.DeadlineAtUtc - (s.CompletedAtUtc ?? now)).TotalSeconds));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 }
        || exception.InnerException?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
            && exception.InnerException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(exception.InnerException) is int code
            && code is 1555 or 2067;
}
