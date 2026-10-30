using System.Text.Json;
using System.Text.RegularExpressions;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Projects;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Services;

public sealed class ProjectService(FlowOpsDbContext dbContext) : IProjectService
{
    private const int MaximumPageSize = 100;
    private static readonly Regex KeyPattern = new("^[A-Z][A-Z0-9]{1,9}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<ProjectResponse> CreateAsync(Guid actorUserId, Guid organizationId, CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Name, request.Description);
        if (!KeyPattern.IsMatch(request.Key))
        {
            throw new ArgumentException("Project key must be 2-10 uppercase letters or numbers and start with a letter.");
        }

        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureRole(membership, "Admin", "TeamLead");
        if (await dbContext.Projects.AnyAsync(project => project.OrganizationId == organizationId && project.Key == request.Key, cancellationToken))
        {
            throw new ConflictException("A project with this key already exists in the organization.");
        }

        var project = new Project
        {
            OrganizationId = organizationId,
            Name = request.Name.Trim(),
            Key = request.Key,
            Description = NormalizeDescription(request.Description),
            CreatedAtUtc = DateTime.UtcNow
        };
        dbContext.Projects.Add(project);
        AddAudit(organizationId, actorUserId, "Project.Created", project.Id, JsonSerializer.Serialize(new { project.Key }));
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsProjectKeyConflict(exception))
        {
            throw new ConflictException("A project with this key already exists in the organization.");
        }

        return Map(project);
    }

    public async Task<PagedResponse<ProjectResponse>> ListAsync(Guid actorUserId, Guid organizationId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(page, pageSize);
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        var query = dbContext.Projects.AsNoTracking().Where(project => project.OrganizationId == organizationId && project.IsActive);
        var total = await query.LongCountAsync(cancellationToken);
        var items = await query.OrderBy(project => project.Name).ThenBy(project => project.Id)
            .Skip(offset).Take(pageSize).Select(project => Map(project)).ToListAsync(cancellationToken);
        return new PagedResponse<ProjectResponse>(items, page, pageSize, total);
    }

    public async Task<ProjectResponse> GetAsync(Guid actorUserId, Guid organizationId, Guid projectId, CancellationToken cancellationToken = default)
    {
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        var project = await dbContext.Projects.AsNoTracking().SingleOrDefaultAsync(
            item => item.OrganizationId == organizationId && item.Id == projectId && item.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The project was not found.");
        return Map(project);
    }

    public async Task<ProjectResponse> UpdateAsync(Guid actorUserId, Guid organizationId, Guid projectId, UpdateProjectRequest request, CancellationToken cancellationToken = default)
    {
        Validate(request.Name, request.Description);
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureRole(membership, "Admin", "TeamLead");
        var project = await GetProjectForUpdateAsync(organizationId, projectId, cancellationToken);
        project.Name = request.Name.Trim();
        project.Description = NormalizeDescription(request.Description);
        project.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(organizationId, actorUserId, "Project.Updated", project.Id, JsonSerializer.Serialize(new { project.Name }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(project);
    }

    public async Task DeactivateAsync(Guid actorUserId, Guid organizationId, Guid projectId, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureRole(membership, "Admin");
        var project = await GetProjectForUpdateAsync(organizationId, projectId, cancellationToken);
        project.IsActive = false;
        project.UpdatedAtUtc = DateTime.UtcNow;
        AddAudit(organizationId, actorUserId, "Project.Deactivated", project.Id, null);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<OrganizationMembership> GetMembershipAsync(Guid actorUserId, Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationMemberships.Include(item => item.Role)
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.UserId == actorUserId
                && item.IsActive && item.User.IsActive && item.Organization.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The organization was not found.");

    private async Task<Project> GetProjectForUpdateAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken) =>
        await dbContext.Projects.SingleOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == projectId && item.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The project was not found.");

    private static void EnsureRole(OrganizationMembership membership, params string[] allowedRoles)
    {
        if (!allowedRoles.Contains(membership.Role.Name, StringComparer.Ordinal))
        {
            throw new ForbiddenException("Your organization role does not allow this project operation.");
        }
    }

    private void AddAudit(Guid organizationId, Guid actorUserId, string action, Guid projectId, string? detailsJson) =>
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = "Project",
            EntityId = projectId,
            DetailsJson = detailsJson,
            OccurredAtUtc = DateTime.UtcNow
        });

    private static ProjectResponse Map(Project project) => new(project.Id, project.OrganizationId, project.Name, project.Key,
        project.Description, project.IsActive, project.CreatedAtUtc, project.UpdatedAtUtc);

    private static int GetOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize || (long)(page - 1) * pageSize > int.MaxValue)
            throw new ArgumentException($"Page must be positive, pageSize must be between 1 and {MaximumPageSize}, and the offset must be supported.");
        return (page - 1) * pageSize;
    }

    private static void Validate(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 2 or > 120)
            throw new ArgumentException("Project name must be 2-120 characters.");
        if (description?.Length > 1000)
            throw new ArgumentException("Project description must be 1000 characters or fewer.");
    }

    private static string? NormalizeDescription(string? description) => string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static bool IsProjectKeyConflict(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 }
        || exception.InnerException?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
            && exception.InnerException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(exception.InnerException) is int code
            && code is 1555 or 2067;
}
