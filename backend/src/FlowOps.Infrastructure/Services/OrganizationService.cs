using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Organizations;
using FlowOps.Domain.Entities;
using FlowOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Services;

public sealed class OrganizationService(FlowOpsDbContext dbContext) : IOrganizationService
{
    private const int MaximumPageSize = 100;
    private const string AdminRoleName = "Admin";
    private static readonly string[] AllowedRoles = ["Admin", "TeamLead", "Engineer", "QA", "Viewer"];
    private static readonly Regex SlugPattern = new("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<OrganizationResponse> CreateAsync(
        Guid actorUserId,
        CreateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganization(request.Name, request.Slug, request.Description);
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (await dbContext.Organizations.AnyAsync(organization => organization.Slug == slug, cancellationToken))
        {
            throw new ConflictException("An organization with this slug already exists.");
        }

        var actorIsActive = await dbContext.Users.AnyAsync(
            user => user.Id == actorUserId && user.IsActive,
            cancellationToken);
        if (!actorIsActive)
        {
            throw new UnauthorizedAccessException("The current account is inactive.");
        }

        var adminRole = await dbContext.Roles.SingleAsync(role => role.Name == AdminRoleName, cancellationToken);
        var now = DateTime.UtcNow;
        var organization = new Organization
        {
            Name = request.Name.Trim(),
            Slug = slug,
            Description = NormalizeDescription(request.Description),
            CreatedAtUtc = now
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        dbContext.Organizations.Add(organization);
        dbContext.OrganizationMemberships.Add(new OrganizationMembership
        {
            Organization = organization,
            UserId = actorUserId,
            RoleId = adminRole.Id,
            IsActive = true,
            JoinedAtUtc = now
        });
        AddAuditEvent(organization.Id, actorUserId, "Organization.Created", "Organization", organization.Id,
            JsonSerializer.Serialize(new { organization.Slug }));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception)
            && exception.Entries.Any(entry => entry.Entity is Organization))
        {
            throw new ConflictException("An organization with this slug already exists.");
        }

        return MapOrganization(organization, adminRole.Name);
    }

    public async Task<PagedResponse<OrganizationResponse>> ListForUserAsync(
        Guid actorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(page, pageSize);
        var memberships = dbContext.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.UserId == actorUserId
                && membership.IsActive
                && membership.User.IsActive
                && membership.Organization.IsActive);

        var totalCount = await memberships.LongCountAsync(cancellationToken);
        var items = await memberships
            .OrderBy(membership => membership.Organization.Name)
            .ThenBy(membership => membership.OrganizationId)
            .Skip(offset)
            .Take(pageSize)
            .Select(membership => new OrganizationResponse(
                membership.Organization.Id,
                membership.Organization.Name,
                membership.Organization.Slug,
                membership.Organization.Description,
                membership.Role.Name,
                membership.Organization.CreatedAtUtc,
                membership.Organization.UpdatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PagedResponse<OrganizationResponse>(items, page, pageSize, totalCount);
    }

    public async Task<OrganizationResponse> GetAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        return MapOrganization(membership.Organization, membership.Role.Name);
    }

    public async Task<OrganizationResponse> UpdateAsync(
        Guid actorUserId,
        Guid organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateOrganizationNameAndDescription(request.Name, request.Description);
        var membership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureAdmin(membership);

        membership.Organization.Name = request.Name.Trim();
        membership.Organization.Description = NormalizeDescription(request.Description);
        membership.Organization.UpdatedAtUtc = DateTime.UtcNow;
        AddAuditEvent(organizationId, actorUserId, "Organization.Updated", "Organization", organizationId,
            JsonSerializer.Serialize(new { membership.Organization.Name }));
        await dbContext.SaveChangesAsync(cancellationToken);

        return MapOrganization(membership.Organization, membership.Role.Name);
    }

    public async Task DeactivateAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var membership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureAdmin(membership);

        membership.Organization.IsActive = false;
        membership.Organization.UpdatedAtUtc = DateTime.UtcNow;
        membership.IsActive = false;
        AddAuditEvent(organizationId, actorUserId, "Organization.Deactivated", "Organization", organizationId, null);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResponse<OrganizationMemberResponse>> ListMembersAsync(
        Guid actorUserId,
        Guid organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(page, pageSize);
        await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);

        var members = dbContext.OrganizationMemberships
            .AsNoTracking()
            .Where(membership => membership.OrganizationId == organizationId);
        var totalCount = await members.LongCountAsync(cancellationToken);
        var items = await members
            .OrderBy(membership => membership.User.Email)
            .ThenBy(membership => membership.UserId)
            .Skip(offset)
            .Take(pageSize)
            .Select(membership => new OrganizationMemberResponse(
                membership.UserId,
                membership.User.Email,
                membership.User.FullName,
                membership.Role.Name,
                membership.JoinedAtUtc,
                membership.IsActive && membership.User.IsActive))
            .ToListAsync(cancellationToken);

        return new PagedResponse<OrganizationMemberResponse>(items, page, pageSize, totalCount);
    }

    public async Task<OrganizationMemberResponse> AddMemberAsync(
        Guid actorUserId,
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateEmail(request.Email);
        var membership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureAdmin(membership);

        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users.SingleOrDefaultAsync(
            existingUser => existingUser.Email == normalizedEmail && existingUser.IsActive,
            cancellationToken);
        if (user is null)
        {
            throw new KeyNotFoundException("An active account with this email was not found.");
        }

        var role = await ResolveRoleAsync(request.Role, cancellationToken);
        var existingMembership = await dbContext.OrganizationMemberships.SingleOrDefaultAsync(
            existing => existing.OrganizationId == organizationId && existing.UserId == user.Id,
            cancellationToken);
        var now = DateTime.UtcNow;

        if (existingMembership is { IsActive: true })
        {
            throw new ConflictException("This user is already an active member of the organization.");
        }

        if (existingMembership is null)
        {
            existingMembership = new OrganizationMembership
            {
                OrganizationId = organizationId,
                UserId = user.Id,
                RoleId = role.Id,
                JoinedAtUtc = now,
                IsActive = true
            };
            dbContext.OrganizationMemberships.Add(existingMembership);
        }
        else
        {
            existingMembership.RoleId = role.Id;
            existingMembership.JoinedAtUtc = now;
            existingMembership.IsActive = true;
        }

        AddAuditEvent(organizationId, actorUserId, "Membership.Added", "User", user.Id,
            JsonSerializer.Serialize(new { role = role.Name }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return MapMember(user, role, existingMembership);
    }

    public async Task<OrganizationMemberResponse> UpdateMemberRoleAsync(
        Guid actorUserId,
        Guid organizationId,
        Guid memberUserId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var actorMembership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureAdmin(actorMembership);

        var targetMembership = await GetTargetMembershipAsync(organizationId, memberUserId, cancellationToken);
        if (!targetMembership.IsActive || !targetMembership.User.IsActive)
        {
            throw new KeyNotFoundException("The active organization member was not found.");
        }

        var newRole = await ResolveRoleAsync(roleName, cancellationToken);
        if (targetMembership.RoleId == newRole.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return MapMember(targetMembership.User, newRole, targetMembership);
        }

        if (targetMembership.Role.Name == AdminRoleName && newRole.Name != AdminRoleName)
        {
            await EnsureAnotherAdminExistsAsync(organizationId, memberUserId, cancellationToken);
        }

        var previousRole = targetMembership.Role.Name;
        targetMembership.RoleId = newRole.Id;
        AddAuditEvent(organizationId, actorUserId, "Membership.RoleChanged", "User", memberUserId,
            JsonSerializer.Serialize(new { previousRole, newRole = newRole.Name }));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return MapMember(targetMembership.User, newRole, targetMembership);
    }

    public async Task DeactivateMemberAsync(
        Guid actorUserId,
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var actorMembership = await GetActiveMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureAdmin(actorMembership);

        var targetMembership = await GetTargetMembershipAsync(organizationId, memberUserId, cancellationToken);
        if (!targetMembership.IsActive)
        {
            throw new KeyNotFoundException("The active organization member was not found.");
        }

        if (targetMembership.Role.Name == AdminRoleName)
        {
            await EnsureAnotherAdminExistsAsync(organizationId, memberUserId, cancellationToken);
        }

        targetMembership.IsActive = false;
        AddAuditEvent(organizationId, actorUserId, "Membership.Deactivated", "User", memberUserId, null);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<OrganizationMembership> GetActiveMembershipAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken cancellationToken) =>
        await dbContext.OrganizationMemberships
            .Include(membership => membership.Organization)
            .Include(membership => membership.Role)
            .SingleOrDefaultAsync(membership => membership.OrganizationId == organizationId
                && membership.UserId == actorUserId
                && membership.IsActive
                && membership.User.IsActive
                && membership.Organization.IsActive,
                cancellationToken)
        ?? throw new KeyNotFoundException("The organization was not found.");

    private async Task<OrganizationMembership> GetTargetMembershipAsync(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken) =>
        await dbContext.OrganizationMemberships
            .Include(membership => membership.User)
            .Include(membership => membership.Role)
            .SingleOrDefaultAsync(membership => membership.OrganizationId == organizationId
                && membership.UserId == memberUserId,
                cancellationToken)
        ?? throw new KeyNotFoundException("The organization member was not found.");

    private async Task EnsureAnotherAdminExistsAsync(
        Guid organizationId,
        Guid currentAdminUserId,
        CancellationToken cancellationToken)
    {
        var anotherActiveAdminExists = await dbContext.OrganizationMemberships.AnyAsync(membership =>
            membership.OrganizationId == organizationId
            && membership.UserId != currentAdminUserId
            && membership.IsActive
            && membership.User.IsActive
            && membership.Role.Name == AdminRoleName,
            cancellationToken);

        if (!anotherActiveAdminExists)
        {
            throw new ConflictException("An organization must retain at least one active administrator.");
        }
    }

    private async Task<Role> ResolveRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var canonicalRoleName = AllowedRoles.FirstOrDefault(allowedRole =>
            string.Equals(allowedRole, roleName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (canonicalRoleName is null)
        {
            throw new ArgumentException("The requested organization role is not supported.");
        }

        return await dbContext.Roles.SingleAsync(role => role.Name == canonicalRoleName, cancellationToken);
    }

    private void EnsureAdmin(OrganizationMembership membership)
    {
        if (!string.Equals(membership.Role.Name, AdminRoleName, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Organization administrator access is required.");
        }
    }

    private void AddAuditEvent(Guid organizationId, Guid actorUserId, string action, string entityType, Guid entityId, string? detailsJson)
    {
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            DetailsJson = detailsJson,
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    private static OrganizationResponse MapOrganization(Organization organization, string currentUserRole) => new(
        organization.Id,
        organization.Name,
        organization.Slug,
        organization.Description,
        currentUserRole,
        organization.CreatedAtUtc,
        organization.UpdatedAtUtc);

    private static OrganizationMemberResponse MapMember(User user, Role role, OrganizationMembership membership) => new(
        user.Id,
        user.Email,
        user.FullName,
        role.Name,
        membership.JoinedAtUtc,
        membership.IsActive && user.IsActive);

    private static int GetOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentException($"Page must be positive and pageSize must be between 1 and {MaximumPageSize}.");
        }

        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            throw new ArgumentException("The requested page is too large.");
        }

        return (int)offset;
    }

    private static void ValidateOrganization(string name, string slug, string? description)
    {
        ValidateOrganizationNameAndDescription(name, description);
        var normalizedSlug = slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedSlug)
            || normalizedSlug.Length is < 3 or > 80
            || !SlugPattern.IsMatch(normalizedSlug))
        {
            throw new ArgumentException("Slug must be 3-80 characters using lowercase letters, numbers, and single hyphens.");
        }
    }

    private static void ValidateOrganizationNameAndDescription(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length is < 2 or > 120)
        {
            throw new ArgumentException("Organization name must be 2-120 characters.");
        }

        if (description?.Length > 500)
        {
            throw new ArgumentException("Organization description must be 500 characters or fewer.");
        }
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Trim().Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email.Trim()))
        {
            throw new ArgumentException("A valid email address of 256 characters or fewer is required.");
        }
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static bool IsUniqueConstraintViolation(DbUpdateException exception)
    {
        if (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return true;
        }

        // SQLite is used by the integration test host, but is intentionally not
        // a production infrastructure dependency. Inspect the provider error
        // without adding its package to the production assembly.
        var innerException = exception.InnerException;
        return innerException?.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
            && innerException.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(innerException) is int extendedErrorCode
            && extendedErrorCode is 1555 or 2067;
    }
}
