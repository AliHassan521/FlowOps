using System.Text.Json;
using System.Text.RegularExpressions;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Releases;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace FlowOps.Infrastructure.Services;

public sealed class ReleaseService(FlowOpsDbContext db, INotificationService notifications, TimeProvider clock) : IReleaseService
{
    private static readonly Regex VersionPattern = new("^[A-Za-z0-9][A-Za-z0-9._+\\-]{0,79}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public async Task<ReleaseEnvironmentResponse> CreateEnvironmentAsync(Guid actor, Guid organization, Guid project, CreateEnvironmentRequest request, CancellationToken ct)
    {
        TenantAccess.Lead(await TenantAccess.RequireAsync(db, actor, organization, project, ct));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length is < 2 or > 80) throw new ArgumentException("Environment name must be 2–80 characters.");
        var name = request.Name.Trim(); var normalized = name.ToLowerInvariant();
        if (await db.ReleaseEnvironments.AnyAsync(e => e.ProjectId == project && e.NormalizedName == normalized, ct)) throw new ConflictException("This environment name already exists, including archived environments.");
        var environment = new ReleaseEnvironment { ProjectId = project, Name = name, NormalizedName = normalized, CreatedAtUtc = clock.GetUtcNow().UtcDateTime };
        db.ReleaseEnvironments.Add(environment); Audit(actor, organization, environment.Id, "ReleaseEnvironment.Created", "ReleaseEnvironment", new { environment.Name });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException e) when (IsUnique(e)) { throw new ConflictException("This environment name already exists."); }
        return new(environment.Id, environment.Name, environment.IsActive, environment.CreatedAtUtc);
    }
    public async Task<PagedResponse<ReleaseEnvironmentResponse>> EnvironmentsAsync(Guid actor, Guid organization, Guid project, int page, int pageSize, bool activeOnly, CancellationToken ct)
    {
        var offset = Offset(page, pageSize); await TenantAccess.RequireAsync(db, actor, organization, project, ct);
        var query = db.ReleaseEnvironments.AsNoTracking().Where(e => e.ProjectId == project && (!activeOnly || e.IsActive));
        var count = await query.LongCountAsync(ct); var rows = await query.OrderBy(e => e.Name).ThenBy(e => e.Id).Skip(offset).Take(pageSize).Select(e => new ReleaseEnvironmentResponse(e.Id, e.Name, e.IsActive, e.CreatedAtUtc)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }
    public async Task ArchiveEnvironmentAsync(Guid actor, Guid organization, Guid project, Guid environment, CancellationToken ct)
    {
        TenantAccess.Lead(await TenantAccess.RequireAsync(db, actor, organization, project, ct));
        var entity = await db.ReleaseEnvironments.SingleOrDefaultAsync(e => e.Id == environment && e.ProjectId == project, ct) ?? throw new KeyNotFoundException("The environment was not found.");
        if (!entity.IsActive) return;
        entity.IsActive = false; Audit(actor, organization, environment, "ReleaseEnvironment.Archived", "ReleaseEnvironment", new { entity.Name }); await db.SaveChangesAsync(ct);
    }
    public async Task<ReleaseResponse> CreateAsync(Guid actor, Guid organization, Guid project, CreateReleaseRequest request, CancellationToken ct)
    {
        TenantAccess.Write(await TenantAccess.RequireAsync(db, actor, organization, project, ct));
        if (string.IsNullOrWhiteSpace(request.ApplicationName) || request.ApplicationName.Trim().Length is < 2 or > 120) throw new ArgumentException("Application name must be 2–120 characters.");
        if (string.IsNullOrWhiteSpace(request.ReleaseVersion) || !VersionPattern.IsMatch(request.ReleaseVersion.Trim())) throw new ArgumentException("Version must be 1–80 letters, digits, dots, underscores, pluses or hyphens, starting with a letter or digit.");
        if (!await db.ReleaseEnvironments.AnyAsync(e => e.Id == request.EnvironmentId && e.ProjectId == project && e.IsActive, ct)) throw new KeyNotFoundException("An active project environment was not found.");
        var now = clock.GetUtcNow().UtcDateTime; var release = new Release { ProjectId = project, EnvironmentId = request.EnvironmentId, ApplicationName = request.ApplicationName.Trim(), ReleaseVersion = request.ReleaseVersion.Trim(), CreatedByUserId = actor, CreatedAtUtc = now };
        release.UpdateNotes(request.Notes, now); db.Releases.Add(release);
        Audit(actor, organization, release.Id, "Release.Created", "Release", new { release.ApplicationName, release.ReleaseVersion, release.EnvironmentId });
        await db.SaveChangesAsync(ct); return await Response(release.Id, ct);
    }
    public async Task<PagedResponse<ReleaseResponse>> ListAsync(Guid actor, Guid organization, Guid project, ReleaseListQuery query, CancellationToken ct)
    {
        var offset = Offset(query.Page, query.PageSize); await TenantAccess.RequireAsync(db, actor, organization, project, ct);
        var releases = db.Releases.AsNoTracking().Where(r => r.ProjectId == project);
        if (!string.IsNullOrWhiteSpace(query.Status)) { var status = Status(query.Status); releases = releases.Where(r => r.Status == status); }
        if (query.EnvironmentId.HasValue) releases = releases.Where(r => r.EnvironmentId == query.EnvironmentId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim(); if (search.Length > 100) throw new ArgumentException("Search must be 100 characters or fewer.");
            releases = releases.Where(r => r.ApplicationName.Contains(search) || r.ReleaseVersion.Contains(search));
        }
        var count = await releases.LongCountAsync(ct); var rows = await releases.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip(offset).Take(query.PageSize).Select(ReleaseReadModel.Project).ToListAsync(ct);
        return new(rows, query.Page, query.PageSize, count);
    }
    public async Task<ReleaseResponse> GetAsync(Guid actor, Guid organization, Guid project, Guid release, CancellationToken ct)
    {
        await TenantAccess.RequireAsync(db, actor, organization, project, ct); await Find(project, release, ct); return await Response(release, ct);
    }
    public async Task<ReleaseResponse> UpdateAsync(Guid actor, Guid organization, Guid project, Guid release, UpdateReleaseRequest request, CancellationToken ct)
    {
        var role = await TenantAccess.RequireAsync(db, actor, organization, project, ct); var entity = await Find(project, release, ct); Edit(role, actor, entity); Version(entity, request.ExpectedVersion);
        try { entity.UpdateNotes(request.Notes, clock.GetUtcNow().UtcDateTime); } catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        Audit(actor, organization, release, "Release.NotesUpdated", "Release", new { hasNotes = entity.Notes != null }); await db.SaveChangesAsync(ct); return await Response(release, ct);
    }
    public async Task<ReleaseLinksResponse> LinksAsync(Guid actor, Guid organization, Guid project, Guid release, CancellationToken ct)
    {
        await TenantAccess.RequireAsync(db, actor, organization, project, ct); await Find(project, release, ct);
        var work = await db.ReleaseWorkItems.AsNoTracking().Where(l => l.ReleaseId == release && l.WorkItem.ProjectId == project).OrderBy(l => l.WorkItemId).Take(100).Select(l => new ReleaseResourceResponse(l.WorkItemId, l.WorkItem.Title, l.WorkItem.Status.ToString())).ToListAsync(ct);
        var incidents = await db.ReleaseIncidents.AsNoTracking().Where(l => l.ReleaseId == release && l.Incident.ProjectId == project).OrderBy(l => l.IncidentId).Take(100).Select(l => new ReleaseResourceResponse(l.IncidentId, l.Incident.Title, l.Incident.Status.ToString())).ToListAsync(ct);
        return new(work, incidents);
    }
    public async Task<ReleaseResponse> SetLinksAsync(Guid actor, Guid organization, Guid project, Guid release, SetReleaseLinksRequest request, CancellationToken ct)
    {
        var role = await TenantAccess.RequireAsync(db, actor, organization, project, ct); var entity = await Find(project, release, ct); Edit(role, actor, entity); Version(entity, request.ExpectedVersion);
        try { entity.EnsureDraft(); } catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        if (request.WorkItemIds is null || request.IncidentIds is null || request.WorkItemIds.Length > 100 || request.IncidentIds.Length > 100 || request.WorkItemIds.Contains(Guid.Empty) || request.IncidentIds.Contains(Guid.Empty)) throw new ArgumentException("Link at most 100 work items and 100 incidents, with valid IDs.");
        var work = request.WorkItemIds.Distinct().ToHashSet(); var incidents = request.IncidentIds.Distinct().ToHashSet();
        if (await db.WorkItems.CountAsync(w => work.Contains(w.Id) && w.ProjectId == project, ct) != work.Count || await db.Incidents.CountAsync(i => incidents.Contains(i.Id) && i.ProjectId == project, ct) != incidents.Count) throw new KeyNotFoundException("Every linked resource must belong to this project.");
        var oldWork = await db.ReleaseWorkItems.Where(l => l.ReleaseId == release).ToListAsync(ct); var oldIncidents = await db.ReleaseIncidents.Where(l => l.ReleaseId == release).ToListAsync(ct);
        if (work.SetEquals(oldWork.Select(l => l.WorkItemId)) && incidents.SetEquals(oldIncidents.Select(l => l.IncidentId))) return await Response(release, ct);
        db.ReleaseWorkItems.RemoveRange(oldWork.Where(l => !work.Contains(l.WorkItemId))); db.ReleaseIncidents.RemoveRange(oldIncidents.Where(l => !incidents.Contains(l.IncidentId)));
        foreach (var id in work.Except(oldWork.Select(l => l.WorkItemId))) db.ReleaseWorkItems.Add(new() { ReleaseId = release, WorkItemId = id });
        foreach (var id in incidents.Except(oldIncidents.Select(l => l.IncidentId))) db.ReleaseIncidents.Add(new() { ReleaseId = release, IncidentId = id });
        db.Entry(entity).Property(r => r.Version).IsModified = true;
        Audit(actor, organization, release, "Release.LinksUpdated", "Release", new { workItemCount = work.Count, incidentCount = incidents.Count });
        try { await db.SaveChangesAsync(ct); } catch (DbUpdateException e) when (IsUnique(e)) { throw new ConflictException("The release links changed concurrently. Refresh and try again."); }
        return await Response(release, ct);
    }
    public async Task<ReleaseResponse> TransitionAsync(Guid actor, Guid organization, Guid project, Guid release, TransitionReleaseRequest request, CancellationToken ct)
    {
        TenantAccess.Lead(await TenantAccess.RequireAsync(db, actor, organization, project, ct)); var entity = await Find(project, release, ct); Version(entity, request.ExpectedVersion); var next = Status(request.Status);
        if (next is ReleaseStatus.Approved or ReleaseStatus.Deploying && !entity.Environment.IsActive) throw new ConflictException("This environment is archived; a new deployment cannot start.");
        var previous = entity.Status;
        try { entity.TransitionTo(next, actor, request.Outcome, clock.GetUtcNow().UtcDateTime); } catch (InvalidOperationException e) { throw new ConflictException(e.Message); }
        var audit = Audit(actor, organization, release, "Release.StatusChanged", "Release", new { from = previous.ToString(), to = next.ToString(), entity.EnvironmentId, entity.Outcome });
        var recipients = await db.OrganizationMemberships.Where(m => m.OrganizationId == organization && m.IsActive && m.User.IsActive && (m.Role.Name == "Admin" || m.Role.Name == "TeamLead" || m.UserId == entity.CreatedByUserId)).Select(m => m.UserId).ToListAsync(ct);
        var title = $"{entity.ApplicationName} {entity.ReleaseVersion} · {entity.Environment.Name}";
        await notifications.QueueReleaseAsync(actor, organization, project, release, audit.Id, title, recipients, ct);
        await db.SaveChangesAsync(ct); await notifications.PublishPendingAsync(); return await Response(release, ct);
    }
    public async Task<PagedResponse<ReleaseTimelineResponse>> TimelineAsync(Guid actor, Guid organization, Guid project, Guid release, int page, int pageSize, CancellationToken ct)
    {
        var offset = Offset(page, pageSize); await TenantAccess.RequireAsync(db, actor, organization, project, ct); await Find(project, release, ct);
        var query = db.AuditEvents.AsNoTracking().Where(a => a.OrganizationId == organization && a.EntityType == "Release" && a.EntityId == release); var count = await query.LongCountAsync(ct);
        var rows = await query.OrderBy(a => a.OccurredAtUtc).ThenBy(a => a.Id).Skip(offset).Take(pageSize).Select(a => new ReleaseTimelineResponse(a.Id, a.ActorUserId, a.Actor == null ? "System" : a.Actor.FullName, a.Action, a.OccurredAtUtc)).ToListAsync(ct);
        return new(rows, page, pageSize, count);
    }
    private async Task<Release> Find(Guid project, Guid release, CancellationToken ct) => await db.Releases.Include(r => r.Environment).SingleOrDefaultAsync(r => r.Id == release && r.ProjectId == project, ct) ?? throw new KeyNotFoundException("The release was not found.");
    private Task<ReleaseResponse> Response(Guid release, CancellationToken ct) => db.Releases.AsNoTracking().Where(r => r.Id == release).Select(ReleaseReadModel.Project).SingleAsync(ct);
    private AuditEvent Audit(Guid actor, Guid organization, Guid id, string action, string type, object details) { var audit = new AuditEvent { ActorUserId = actor, OrganizationId = organization, EntityId = id, EntityType = type, Action = action, DetailsJson = JsonSerializer.Serialize(details), OccurredAtUtc = clock.GetUtcNow().UtcDateTime }; db.AuditEvents.Add(audit); return audit; }
    private static void Version(Release release, Guid expected) { if (expected == Guid.Empty || release.Version != expected) throw new ConflictException("The release changed. Refresh before trying again."); }
    private static void Edit(string role, Guid actor, Release release) { TenantAccess.Write(role); if (!TenantAccess.IsLead(role) && release.CreatedByUserId != actor) throw new ForbiddenException("Only the draft creator or an organization lead may edit this release."); }
    private static ReleaseStatus Status(string value) => Enum.GetNames<ReleaseStatus>().FirstOrDefault(n => n.Equals(value?.Trim(), StringComparison.OrdinalIgnoreCase)) is string name ? Enum.Parse<ReleaseStatus>(name) : throw new ArgumentException("Status must be Draft, Approved, Deploying, Successful or Failed.");
    private static int Offset(int page, int size) { if (page < 1 || size is < 1 or > 100 || (long)(page - 1) * size > int.MaxValue) throw new ArgumentException("Page must be positive, pageSize 1–100 and the offset supported."); return (page - 1) * size; }
    private static bool IsUnique(DbUpdateException e) => e.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } || e.InnerException?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException" && e.InnerException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(e.InnerException) is int code && code is 1555 or 2067;
}
