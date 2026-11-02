using System.Text.Json;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Incidents;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using FlowOps.Infrastructure.Persistence;
namespace FlowOps.Infrastructure.Services;

public sealed class IncidentService(FlowOpsDbContext db, INotificationService notifications, TimeProvider clock) : IIncidentService
{
    public async Task<IncidentResponse> CreateAsync(Guid actor, Guid organization, Guid project, CreateIncidentRequest request, CancellationToken ct)
    {
        var role = await Access(actor, organization, ct); Write(role);
        await Project(organization, project, ct);
        Details(request.Title, request.Description, request.AffectedService);
        var severity = Parse<IncidentSeverity>(request.Severity);
        if (request.OwnerUserId.HasValue) { Lead(role); await Owner(organization, request.OwnerUserId.Value, ct); }
        var now = clock.GetUtcNow().UtcDateTime;
        var incident = new Incident { ProjectId = project, ReporterUserId = actor, StartedAtUtc = now };
        incident.UpdateDetails(request.Title.Trim(), Clean(request.Description), request.AffectedService.Trim(), severity, now);
        incident.SetOwner(request.OwnerUserId, now);
        db.Incidents.Add(incident);
        var audit = Audit(actor, organization, incident.Id, "Incident.Created", new { incident.Severity, incident.OwnerUserId });
        if (incident.OwnerUserId is Guid owner) await Notify(actor, organization, incident, audit.Id, "Incident.Assigned", [owner], ct);
        await Save(ct);
        return await Response(incident.Id, ct);
    }
    public async Task<PagedResponse<IncidentResponse>> ListAsync(Guid actor, Guid organization, Guid project, IncidentListQuery query, CancellationToken ct)
    {
        var offset = Offset(query.Page, query.PageSize);
        await Access(actor, organization, ct); await Project(organization, project, ct);
        var items = db.Incidents.AsNoTracking().Where(i => i.ProjectId == project);
        if (!string.IsNullOrWhiteSpace(query.Severity)) { var severity = Parse<IncidentSeverity>(query.Severity); items = items.Where(i => i.Severity == severity); }
        if (!string.IsNullOrWhiteSpace(query.Status)) { var status = Parse<IncidentStatus>(query.Status); items = items.Where(i => i.Status == status); }
        if (query.OwnerUserId.HasValue) items = items.Where(i => i.OwnerUserId == query.OwnerUserId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim(); if (search.Length > 100) throw new ArgumentException("Search must be 100 characters or fewer.");
            items = items.Where(i => i.Title.Contains(search) || i.AffectedService.Contains(search));
        }
        var count = await items.LongCountAsync(ct);
        var page = await items.Include(i => i.Reporter).Include(i => i.Owner).OrderByDescending(i => i.StartedAtUtc).ThenBy(i => i.Id)
            .Skip(offset).Take(query.PageSize).ToListAsync(ct);
        return new(page.Select(Map).ToArray(), query.Page, query.PageSize, count);
    }
    public async Task<IncidentResponse> GetAsync(Guid actor, Guid organization, Guid project, Guid incident, CancellationToken ct)
    {
        await Access(actor, organization, ct); return Map(await Find(organization, project, incident, ct));
    }
    public async Task<IncidentResponse> UpdateAsync(Guid actor, Guid organization, Guid project, Guid incident, UpdateIncidentRequest request, CancellationToken ct)
    {
        var role = await Access(actor, organization, ct); Write(role);
        var item = await Find(organization, project, incident, ct); Edit(role, actor, item); Version(item, request.ExpectedVersion);
        Details(request.Title, request.Description, request.AffectedService);
        var severity = Parse<IncidentSeverity>(request.Severity);
        try { item.UpdateDetails(request.Title.Trim(), Clean(request.Description), request.AffectedService.Trim(), severity, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        Audit(actor, organization, incident, "Incident.Updated", new { item.Title, item.Severity, item.AffectedService });
        await Save(ct); return await Response(incident, ct);
    }
    public async Task<IncidentResponse> AssignAsync(Guid actor, Guid organization, Guid project, Guid incident, AssignIncidentRequest request, CancellationToken ct)
    {
        Lead(await Access(actor, organization, ct));
        var item = await Find(organization, project, incident, ct); Version(item, request.ExpectedVersion);
        if (request.OwnerUserId.HasValue) await Owner(organization, request.OwnerUserId.Value, ct);
        if (item.OwnerUserId == request.OwnerUserId) return Map(item);
        var previous = item.OwnerUserId;
        try { item.SetOwner(request.OwnerUserId, clock.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        var audit = Audit(actor, organization, incident, "Incident.Assigned", new { previousOwnerUserId = previous, item.OwnerUserId });
        if (item.OwnerUserId is Guid owner) await Notify(actor, organization, item, audit.Id, "Incident.Assigned", [owner], ct);
        await Save(ct); return await Response(incident, ct);
    }
    public async Task<IncidentResponse> TransitionAsync(Guid actor, Guid organization, Guid project, Guid incident, TransitionIncidentRequest request, CancellationToken ct)
    {
        var role = await Access(actor, organization, ct); Write(role);
        var item = await Find(organization, project, incident, ct); Version(item, request.ExpectedVersion);
        var next = Parse<IncidentStatus>(request.Status);
        if (next == IncidentStatus.Closed) Lead(role);
        else if (!IsLead(role) && item.OwnerUserId != actor) throw new ForbiddenException("Only the incident owner or an organization lead may change its status.");
        var previous = item.Status; var now = clock.GetUtcNow().UtcDateTime;
        try { item.TransitionTo(next, actor, request.Resolution, request.RootCause, now); }
        catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        var audit = Audit(actor, organization, incident, "Incident.StatusChanged", new { previousStatus = previous.ToString(), status = next.ToString(), item.Resolution, item.RootCause });
        await Notify(actor, organization, item, audit.Id, "Incident.StatusChanged", Recipients(item), ct);
        if (next == IncidentStatus.Resolved)
        {
            var sla = await db.IncidentSlas.Include(s => s.Incident).ThenInclude(i => i.Project).SingleOrDefaultAsync(s => s.IncidentId == incident, ct);
            if (sla is not null && !sla.CompletedAtUtc.HasValue)
            {
                if (sla.Complete(now)) await IncidentSlaEvents.StageAsync(db, notifications, sla, "Incident.SlaBreached", now, ct);
                Audit(null, organization, incident, "Incident.SlaCompleted", new { sla.CompletedAtUtc, sla.BreachedAtUtc });
            }
        }
        await Save(ct); return await Response(incident, ct);
    }
    public async Task<IncidentCommentResponse> CommentAsync(Guid actor, Guid organization, Guid project, Guid incident, AddIncidentCommentRequest request, CancellationToken ct)
    {
        Write(await Access(actor, organization, ct)); var item = await Find(organization, project, incident, ct);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > 4000) throw new ArgumentException("Comment must be 1–4000 characters.");
        var comment = new IncidentComment { IncidentId = incident, AuthorUserId = actor, Body = request.Body.Trim(), CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.IncidentComments.Add(comment);
        var audit = Audit(actor, organization, incident, "Incident.CommentAdded", new { commentId = comment.Id });
        await Notify(actor, organization, item, audit.Id, "Incident.CommentAdded", Recipients(item), ct);
        await Save(ct);
        var name = await db.Users.Where(u => u.Id == actor).Select(u => u.FullName).SingleAsync(ct);
        return new(comment.Id, actor, name, comment.Body, comment.CreatedAtUtc);
    }
    public async Task<PagedResponse<IncidentCommentResponse>> CommentsAsync(Guid actor, Guid organization, Guid project, Guid incident, int page, int pageSize, CancellationToken ct)
    {
        var offset = Offset(page, pageSize); await Access(actor, organization, ct); await Find(organization, project, incident, ct);
        var query = db.IncidentComments.AsNoTracking().Where(c => c.IncidentId == incident);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(c => c.CreatedAtUtc).ThenBy(c => c.Id).Skip(offset).Take(pageSize)
            .Select(c => new IncidentCommentResponse(c.Id, c.AuthorUserId, c.Author.FullName, c.Body, c.CreatedAtUtc)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }
    public async Task<PagedResponse<IncidentTimelineResponse>> TimelineAsync(Guid actor, Guid organization, Guid project, Guid incident, int page, int pageSize, CancellationToken ct)
    {
        var offset = Offset(page, pageSize); await Access(actor, organization, ct); await Find(organization, project, incident, ct);
        var query = db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == organization && a.EntityType == "Incident" && a.EntityId == incident);
        var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).Skip(offset).Take(pageSize)
            .Select(a => new IncidentTimelineResponse(a.Id, a.ActorUserId, a.Actor == null ? "System" : a.Actor.FullName, a.Action, a.DetailsJson, a.OccurredAtUtc)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }
    public async Task<IncidentSlaStateResponse> SlaAsync(Guid actor, Guid organization, Guid project, Guid incident, CancellationToken ct)
    {
        await Access(actor, organization, ct); await Find(organization, project, incident, ct);
        return await SlaState(incident, ct);
    }
    public async Task<IncidentSlaStateResponse> AttachSlaAsync(Guid actor, Guid organization, Guid project, Guid incident, AttachIncidentSlaRequest request, CancellationToken ct)
    {
        Lead(await Access(actor, organization, ct)); var item = await Find(organization, project, incident, ct); Version(item, request.ExpectedVersion);
        if (item.Status is IncidentStatus.Resolved or IncidentStatus.Closed) throw new ConflictException("An SLA cannot start on a resolved incident.");
        if (await db.IncidentSlas.AnyAsync(s => s.IncidentId == incident, ct)) throw new ConflictException("This incident already has an SLA; its clock cannot be reset.");
        var policy = await db.SlaPolicies.SingleOrDefaultAsync(p => p.Id == request.PolicyId && p.OrganizationId == organization && p.IsActive, ct)
            ?? throw new KeyNotFoundException("An active SLA policy was not found.");
        var sla = IncidentSla.Start(incident, policy, clock.GetUtcNow().UtcDateTime); db.IncidentSlas.Add(sla);
        db.Entry(item).Property(i => i.Version).IsModified = true;
        Audit(actor, organization, incident, "Incident.SlaAttached", new { sla.Id, sla.PolicyName, sla.DeadlineAtUtc });
        try { await Save(ct); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        { throw new ConflictException("This incident already has an SLA; its clock cannot be reset."); }
        return await SlaState(incident, ct);
    }
    public async Task<IncidentSummaryResponse> SummaryAsync(Guid actor, Guid organization, Guid? project, CancellationToken ct)
    {
        await Access(actor, organization, ct); if (project.HasValue) await Project(organization, project.Value, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var incidents = db.Incidents.AsNoTracking().Where(i => i.Project.OrganizationId == organization && i.Project.IsActive && i.Project.Organization.IsActive
            && (!project.HasValue || i.ProjectId == project.Value));
        var counts = await incidents.GroupBy(i => 1).Select(g => new
        {
            Active = g.LongCount(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed),
            Critical = g.LongCount(i => i.Severity == IncidentSeverity.Critical && i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed),
            Resolved = g.LongCount(i => i.Status == IncidentStatus.Resolved || i.Status == IncidentStatus.Closed)
        }).SingleOrDefaultAsync(ct);
        var measurements = await db.IncidentSlas.AsNoTracking().Where(s => incidents.Select(i => i.Id).Contains(s.IncidentId)).GroupBy(s => 1)
            .Select(g => new
            {
                Risk = g.LongCount(s => s.CompletedAtUtc == null && s.BreachedAtUtc == null && s.WarningAtUtc != null && s.WarningAtUtc <= now && s.DeadlineAtUtc > now),
                Breached = g.LongCount(s => s.BreachedAtUtc != null || s.CompletedAtUtc == null && s.DeadlineAtUtc <= now)
            }).SingleOrDefaultAsync(ct);
        return new(counts?.Active ?? 0, counts?.Critical ?? 0, counts?.Resolved ?? 0, measurements?.Risk ?? 0, measurements?.Breached ?? 0);
    }
    private async Task<string> Access(Guid actor, Guid organization, CancellationToken ct) =>
        await db.OrganizationMemberships.Where(m => m.OrganizationId == organization && m.UserId == actor && m.IsActive && m.User.IsActive && m.Organization.IsActive)
            .Select(m => m.Role.Name).SingleOrDefaultAsync(ct) ?? throw new KeyNotFoundException("The organization was not found.");
    private async Task Project(Guid organization, Guid project, CancellationToken ct)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == project && p.OrganizationId == organization && p.IsActive && p.Organization.IsActive, ct))
            throw new KeyNotFoundException("The project was not found.");
    }
    private async Task<Incident> Find(Guid organization, Guid project, Guid incident, CancellationToken ct) =>
        await db.Incidents.Include(i => i.Reporter).Include(i => i.Owner).Include(i => i.Project)
            .SingleOrDefaultAsync(i => i.Id == incident && i.ProjectId == project && i.Project.OrganizationId == organization && i.Project.IsActive && i.Project.Organization.IsActive, ct)
        ?? throw new KeyNotFoundException("The incident was not found.");
    private async Task Owner(Guid organization, Guid owner, CancellationToken ct)
    {
        if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organization && m.UserId == owner && m.IsActive && m.User.IsActive && m.Organization.IsActive && m.Role.Name != "Viewer", ct))
            throw new KeyNotFoundException("An active writable organization member was not found.");
    }
    private AuditEvent Audit(Guid? actor, Guid organization, Guid incident, string action, object details)
    {
        var audit = new AuditEvent
        {
            ActorUserId = actor,
            OrganizationId = organization,
            EntityId = incident,
            EntityType = "Incident",
            Action = action,
            DetailsJson = JsonSerializer.Serialize(details),
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime
        }; db.AuditEvents.Add(audit); return audit;
    }
    private Task Notify(Guid? actor, Guid organization, Incident item, Guid audit, string kind, IEnumerable<Guid> recipients, CancellationToken ct) =>
        notifications.QueueIncidentAsync(actor, organization, item.ProjectId, item.Id, audit, kind, item.Title, recipients, ct);
    private async Task Save(CancellationToken ct) { await db.SaveChangesAsync(ct); await notifications.PublishPendingAsync(); }
    private async Task<IncidentResponse> Response(Guid id, CancellationToken ct) => Map(await db.Incidents.AsNoTracking().Include(i => i.Reporter).Include(i => i.Owner).SingleAsync(i => i.Id == id, ct));
    private async Task<IncidentSlaStateResponse> SlaState(Guid id, CancellationToken ct)
    {
        var s = await db.IncidentSlas.AsNoTracking().SingleOrDefaultAsync(s => s.IncidentId == id, ct); var now = clock.GetUtcNow().UtcDateTime;
        if (s is null) return new(null, now);
        var status = s.BreachedAtUtc.HasValue ? "Breached" : s.CompletedAtUtc.HasValue ? "Met" : now >= s.DeadlineAtUtc ? "Overdue" : s.WarningAtUtc.HasValue && now >= s.WarningAtUtc ? "AtRisk" : "Active";
        return new(new(s.Id, s.PolicyId, s.PolicyName, s.DurationMinutes, s.WarningBeforeMinutes, s.StartedAtUtc, s.DeadlineAtUtc, s.WarningAtUtc, s.WarnedAtUtc, s.BreachedAtUtc, s.CompletedAtUtc, status,
            (long)Math.Ceiling((s.DeadlineAtUtc - (s.CompletedAtUtc ?? now)).TotalSeconds)), now);
    }
    private static IncidentResponse Map(Incident i) => new(i.Id, i.Number, i.Version, i.ProjectId, i.Title, i.Description, i.AffectedService, i.Severity.ToString(), i.Status.ToString(),
        i.ReporterUserId, i.Reporter.FullName, i.OwnerUserId, i.Owner?.FullName, i.StartedAtUtc, i.UpdatedAtUtc, i.EndedAtUtc, i.ResolvedByUserId, i.Resolution, i.RootCause);
    private static IEnumerable<Guid> Recipients(Incident i) => new[] { i.ReporterUserId }.Concat(i.OwnerUserId is Guid owner ? [owner] : []);
    private static bool IsLead(string role) => role is "Admin" or "TeamLead";
    private static void Lead(string role) { if (!IsLead(role)) throw new ForbiddenException("An organization admin or team lead must perform this action."); }
    private static void Write(string role) { if (role == "Viewer") throw new ForbiddenException("Your organization role is read-only."); }
    private static void Edit(string role, Guid actor, Incident i) { if (!IsLead(role) && i.ReporterUserId != actor && i.OwnerUserId != actor) throw new ForbiddenException("Only the reporter, owner or an organization lead can edit this incident."); }
    private static void Version(Incident i, Guid version) { if (version == Guid.Empty || i.Version != version) throw new ConflictException("The incident changed. Refresh before trying again."); }
    private static T Parse<T>(string value) where T : struct, Enum => Enum.GetNames<T>().FirstOrDefault(n => n.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) is string name
        ? Enum.Parse<T>(name) : throw new ArgumentException($"Value must be one of: {string.Join(", ", Enum.GetNames<T>())}.");
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 }
        || exception.InnerException?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
            && exception.InnerException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(exception.InnerException) is int code
            && code is 1555 or 2067;
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static void Details(string title, string? description, string service)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length is < 2 or > 200 || description?.Length > 8000
            || string.IsNullOrWhiteSpace(service) || service.Trim().Length is < 2 or > 120) throw new ArgumentException("Title must be 2–200 characters, affected service 2–120 and description at most 8000.");
    }
    private static int Offset(int page, int size)
    {
        if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue) throw new ArgumentException("Page must be positive, pageSize 1–100 and the offset supported.");
        return (page - 1) * size;
    }
}
