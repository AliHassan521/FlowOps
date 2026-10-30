using System.Text.Json;
using FlowOps.Application.Common.Exceptions;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.WorkItems;
using FlowOps.Domain.Entities;
using FlowOps.Domain.Enums;
using FlowOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Services;

public sealed class WorkItemService(FlowOpsDbContext dbContext) : IWorkItemService
{
    private const int MaximumPageSize = 100;
    private const int MaximumTitleLength = 200;
    private const int MaximumDescriptionLength = 8000;
    private const int MaximumCommentLength = 4000;
    private const string Admin = "Admin";
    private const string TeamLead = "TeamLead";
    private const string Engineer = "Engineer";
    private const string Qa = "QA";
    private const string Viewer = "Viewer";

    public async Task<WorkItemResponse> CreateAsync(Guid actorUserId, Guid organizationId, Guid projectId,
        CreateWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureCanWrite(membership);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        var type = ParseEnum<WorkItemType>(request.Type, "type");
        var priority = ParseEnum<WorkItemPriority>(request.Priority, "priority");
        ValidateDetails(request.Title, request.Description);
        var dueAtUtc = NormalizeUtc(request.DueAtUtc, "dueAtUtc");
        if (request.AssigneeUserId.HasValue)
        {
            await EnsureAssignableMemberAsync(organizationId, request.AssigneeUserId.Value, cancellationToken);
        }

        var now = DateTime.UtcNow;
        var workItem = new WorkItem
        {
            ProjectId = projectId,
            Type = type,
            Title = request.Title.Trim(),
            Description = NormalizeDescription(request.Description),
            Priority = priority,
            ReporterUserId = actorUserId,
            AssigneeUserId = request.AssigneeUserId,
            DueAtUtc = dueAtUtc,
            CreatedAtUtc = now
        };
        dbContext.WorkItems.Add(workItem);
        AddAudit(organizationId, actorUserId, "WorkItem.Created", workItem.Id,
            JsonSerializer.Serialize(new { type = type.ToString(), priority = priority.ToString(), workItem.AssigneeUserId }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetResponseAsync(workItem.Id, cancellationToken);
    }

    public async Task<PagedResponse<WorkItemResponse>> ListAsync(Guid actorUserId, Guid organizationId, Guid projectId,
        WorkItemListQuery query, CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(query.Page, query.PageSize);
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);

        var items = dbContext.WorkItems.AsNoTracking().Where(workItem => workItem.ProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = ParseEnum<WorkItemType>(query.Type, "type");
            items = items.Where(workItem => workItem.Type == type);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = ParseEnum<WorkItemStatus>(query.Status, "status");
            items = items.Where(workItem => workItem.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            var priority = ParseEnum<WorkItemPriority>(query.Priority, "priority");
            items = items.Where(workItem => workItem.Priority == priority);
        }
        if (query.AssigneeUserId.HasValue)
        {
            items = items.Where(workItem => workItem.AssigneeUserId == query.AssigneeUserId);
        }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            if (query.Search.Trim().Length > 100)
            {
                throw new ArgumentException("Search must be 100 characters or fewer.");
            }
            var search = query.Search.Trim();
            items = items.Where(workItem => workItem.Title.Contains(search)
                || (workItem.Description != null && workItem.Description.Contains(search)));
        }
        if (query.DueBeforeUtc.HasValue)
        {
            var dueBeforeUtc = NormalizeUtc(query.DueBeforeUtc, "dueBeforeUtc");
            items = items.Where(workItem => workItem.DueAtUtc.HasValue && workItem.DueAtUtc <= dueBeforeUtc);
        }

        var totalCount = await items.LongCountAsync(cancellationToken);
        var orderedItems = ApplyOrdering(items, query.SortBy, query.SortDirection);
        var responses = await orderedItems.Skip(offset).Take(query.PageSize)
            .Select(workItem => new WorkItemResponse(
                workItem.Id,
                workItem.ProjectId,
                workItem.Type.ToString(),
                workItem.Title,
                workItem.Description,
                workItem.Priority.ToString(),
                workItem.Status.ToString(),
                workItem.ReporterUserId,
                workItem.Reporter.FullName,
                workItem.AssigneeUserId,
                workItem.Assignee == null ? null : workItem.Assignee.FullName,
                workItem.DueAtUtc,
                workItem.CreatedAtUtc,
                workItem.UpdatedAtUtc,
                workItem.Comments.Count))
            .ToListAsync(cancellationToken);

        return new PagedResponse<WorkItemResponse>(responses, query.Page, query.PageSize, totalCount);
    }

    public async Task<WorkItemResponse> GetAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        CancellationToken cancellationToken = default)
    {
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        await EnsureWorkItemExistsAsync(projectId, workItemId, cancellationToken);
        return await GetResponseAsync(workItemId, cancellationToken, projectId);
    }

    public async Task<WorkItemResponse> UpdateAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        UpdateWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureCanWrite(membership);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        var workItem = await GetTrackedWorkItemAsync(projectId, workItemId, cancellationToken);
        EnsureCanEdit(membership, workItem);
        ValidateDetails(request.Title, request.Description);
        var priority = ParseEnum<WorkItemPriority>(request.Priority, "priority");
        var dueAtUtc = NormalizeUtc(request.DueAtUtc, "dueAtUtc");
        var previous = new { workItem.Title, workItem.Priority, workItem.DueAtUtc };
        workItem.UpdateDetails(request.Title.Trim(), NormalizeDescription(request.Description), priority, dueAtUtc, DateTime.UtcNow);
        AddAudit(organizationId, actorUserId, "WorkItem.Updated", workItemId,
            JsonSerializer.Serialize(new { previous, workItem.Title, priority = priority.ToString(), workItem.DueAtUtc }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetResponseAsync(workItemId, cancellationToken, projectId);
    }

    public async Task<WorkItemResponse> AssignAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        AssignWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureRole(membership, Admin, TeamLead);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        var workItem = await GetTrackedWorkItemAsync(projectId, workItemId, cancellationToken);
        if (request.AssigneeUserId.HasValue)
        {
            await EnsureAssignableMemberAsync(organizationId, request.AssigneeUserId.Value, cancellationToken);
        }
        if (workItem.AssigneeUserId == request.AssigneeUserId)
        {
            return await GetResponseAsync(workItemId, cancellationToken, projectId);
        }

        var previousAssigneeId = workItem.AssigneeUserId;
        workItem.SetAssignee(request.AssigneeUserId, DateTime.UtcNow);
        AddAudit(organizationId, actorUserId, "WorkItem.Assigned", workItemId,
            JsonSerializer.Serialize(new { previousAssigneeUserId = previousAssigneeId, workItem.AssigneeUserId }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetResponseAsync(workItemId, cancellationToken, projectId);
    }

    public async Task<WorkItemResponse> TransitionAsync(Guid actorUserId, Guid organizationId, Guid projectId, Guid workItemId,
        TransitionWorkItemRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        var workItem = await GetTrackedWorkItemAsync(projectId, workItemId, cancellationToken);
        var nextStatus = ParseEnum<WorkItemStatus>(request.Status, "status");
        EnsureCanTransition(membership, workItem, nextStatus);
        var previousStatus = workItem.Status;
        try
        {
            workItem.TransitionTo(nextStatus, DateTime.UtcNow);
        }
        catch (InvalidOperationException exception)
        {
            throw new ConflictException(exception.Message);
        }

        AddAudit(organizationId, actorUserId, "WorkItem.StatusChanged", workItemId,
            JsonSerializer.Serialize(new { from = previousStatus.ToString(), to = nextStatus.ToString() }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetResponseAsync(workItemId, cancellationToken, projectId);
    }

    public async Task<PagedResponse<WorkItemCommentResponse>> ListCommentsAsync(Guid actorUserId, Guid organizationId,
        Guid projectId, Guid workItemId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(page, pageSize);
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        await EnsureWorkItemExistsAsync(projectId, workItemId, cancellationToken);
        var comments = dbContext.WorkItemComments.AsNoTracking().Where(comment => comment.WorkItemId == workItemId);
        var totalCount = await comments.LongCountAsync(cancellationToken);
        var response = await comments.OrderBy(comment => comment.CreatedAtUtc).ThenBy(comment => comment.Id)
            .Skip(offset).Take(pageSize)
            .Select(comment => new WorkItemCommentResponse(comment.Id, comment.AuthorUserId, comment.Author.FullName,
                comment.Body, comment.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResponse<WorkItemCommentResponse>(response, page, pageSize, totalCount);
    }

    public async Task<WorkItemCommentResponse> AddCommentAsync(Guid actorUserId, Guid organizationId, Guid projectId,
        Guid workItemId, AddWorkItemCommentRequest request, CancellationToken cancellationToken = default)
    {
        var membership = await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        EnsureCanWrite(membership);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        await EnsureWorkItemExistsAsync(projectId, workItemId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Body) || request.Body.Trim().Length > MaximumCommentLength)
        {
            throw new ArgumentException($"Comment must contain 1-{MaximumCommentLength} characters.");
        }

        var now = DateTime.UtcNow;
        var comment = new WorkItemComment
        {
            WorkItemId = workItemId,
            AuthorUserId = actorUserId,
            Body = request.Body.Trim(),
            CreatedAtUtc = now
        };
        dbContext.WorkItemComments.Add(comment);
        AddAudit(organizationId, actorUserId, "WorkItem.CommentAdded", workItemId,
            JsonSerializer.Serialize(new { comment.Id }));
        await dbContext.SaveChangesAsync(cancellationToken);
        return await dbContext.WorkItemComments.AsNoTracking()
            .Where(item => item.Id == comment.Id)
            .Select(item => new WorkItemCommentResponse(item.Id, item.AuthorUserId, item.Author.FullName, item.Body, item.CreatedAtUtc))
            .SingleAsync(cancellationToken);
    }

    public async Task<PagedResponse<WorkItemHistoryResponse>> ListHistoryAsync(Guid actorUserId, Guid organizationId,
        Guid projectId, Guid workItemId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var offset = GetOffset(page, pageSize);
        await GetMembershipAsync(actorUserId, organizationId, cancellationToken);
        await EnsureActiveProjectAsync(organizationId, projectId, cancellationToken);
        await EnsureWorkItemExistsAsync(projectId, workItemId, cancellationToken);
        var events = dbContext.AuditEvents.AsNoTracking().Where(auditEvent =>
            auditEvent.OrganizationId == organizationId
            && auditEvent.EntityType == "WorkItem"
            && auditEvent.EntityId == workItemId);
        var totalCount = await events.LongCountAsync(cancellationToken);
        var response = await events.OrderByDescending(auditEvent => auditEvent.OccurredAtUtc).ThenByDescending(auditEvent => auditEvent.Id)
            .Skip(offset).Take(pageSize)
            .Select(auditEvent => new WorkItemHistoryResponse(auditEvent.Id, auditEvent.ActorUserId,
                auditEvent.Actor.FullName, auditEvent.Action, auditEvent.DetailsJson, auditEvent.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResponse<WorkItemHistoryResponse>(response, page, pageSize, totalCount);
    }

    private async Task<OrganizationMembership> GetMembershipAsync(Guid actorUserId, Guid organizationId, CancellationToken cancellationToken) =>
        await dbContext.OrganizationMemberships.Include(membership => membership.Role)
            .SingleOrDefaultAsync(membership => membership.OrganizationId == organizationId
                && membership.UserId == actorUserId && membership.IsActive
                && membership.User.IsActive && membership.Organization.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException("The organization was not found.");

    private async Task EnsureActiveProjectAsync(Guid organizationId, Guid projectId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.Projects.AnyAsync(project => project.Id == projectId
            && project.OrganizationId == organizationId && project.IsActive
            && project.Organization.IsActive, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("The project was not found.");
        }
    }

    private async Task EnsureAssignableMemberAsync(Guid organizationId, Guid assigneeUserId, CancellationToken cancellationToken)
    {
        var canBeAssigned = await dbContext.OrganizationMemberships.AnyAsync(membership =>
            membership.OrganizationId == organizationId && membership.UserId == assigneeUserId
            && membership.IsActive && membership.User.IsActive && membership.Organization.IsActive
            && membership.Role.Name != Viewer, cancellationToken);
        if (!canBeAssigned)
        {
            throw new KeyNotFoundException("An active, assignable organization member was not found.");
        }
    }

    private async Task<WorkItem> GetTrackedWorkItemAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken) =>
        await dbContext.WorkItems.SingleOrDefaultAsync(workItem => workItem.Id == workItemId && workItem.ProjectId == projectId, cancellationToken)
        ?? throw new KeyNotFoundException("The work item was not found.");

    private async Task EnsureWorkItemExistsAsync(Guid projectId, Guid workItemId, CancellationToken cancellationToken)
    {
        if (!await dbContext.WorkItems.AnyAsync(workItem => workItem.Id == workItemId && workItem.ProjectId == projectId, cancellationToken))
        {
            throw new KeyNotFoundException("The work item was not found.");
        }
    }

    private async Task<WorkItemResponse> GetResponseAsync(Guid workItemId, CancellationToken cancellationToken, Guid? projectId = null)
    {
        var query = dbContext.WorkItems.AsNoTracking().Where(workItem => workItem.Id == workItemId);
        if (projectId.HasValue)
        {
            query = query.Where(workItem => workItem.ProjectId == projectId.Value);
        }
        return await query.Select(workItem => new WorkItemResponse(
                workItem.Id, workItem.ProjectId, workItem.Type.ToString(), workItem.Title, workItem.Description,
                workItem.Priority.ToString(), workItem.Status.ToString(), workItem.ReporterUserId, workItem.Reporter.FullName,
                workItem.AssigneeUserId, workItem.Assignee == null ? null : workItem.Assignee.FullName,
                workItem.DueAtUtc, workItem.CreatedAtUtc, workItem.UpdatedAtUtc, workItem.Comments.Count))
            .SingleAsync(cancellationToken);
    }

    private void EnsureCanTransition(OrganizationMembership membership, WorkItem workItem, WorkItemStatus nextStatus)
    {
        var role = membership.Role.Name;
        if (role is Admin or TeamLead) return;

        var assignedEngineer = role == Engineer && workItem.AssigneeUserId == membership.UserId;
        var qaMember = role == Qa;
        var allowed = (workItem.Status, nextStatus) switch
        {
            (WorkItemStatus.New, WorkItemStatus.Triaged) => qaMember,
            (WorkItemStatus.Assigned, WorkItemStatus.InProgress) => assignedEngineer,
            (WorkItemStatus.InProgress, WorkItemStatus.CodeReview) => assignedEngineer,
            (WorkItemStatus.CodeReview, WorkItemStatus.InProgress) => assignedEngineer,
            (WorkItemStatus.CodeReview, WorkItemStatus.QA) => qaMember,
            (WorkItemStatus.QA, WorkItemStatus.InProgress) => qaMember,
            (WorkItemStatus.QA, WorkItemStatus.Resolved) => qaMember,
            _ => false
        };
        if (!allowed)
        {
            throw new ForbiddenException("Your organization role does not allow this workflow transition.");
        }
    }

    private static void EnsureCanWrite(OrganizationMembership membership)
    {
        if (membership.Role.Name == Viewer)
        {
            throw new ForbiddenException("Your organization role is read-only.");
        }
    }

    private static void EnsureCanEdit(OrganizationMembership membership, WorkItem workItem)
    {
        if (membership.Role.Name is Admin or TeamLead
            || workItem.ReporterUserId == membership.UserId
            || workItem.AssigneeUserId == membership.UserId)
        {
            return;
        }
        throw new ForbiddenException("Only the reporter, assignee, or an organization lead can edit this work item.");
    }

    private static void EnsureRole(OrganizationMembership membership, params string[] allowedRoles)
    {
        if (!allowedRoles.Contains(membership.Role.Name, StringComparer.Ordinal))
        {
            throw new ForbiddenException("Your organization role does not allow this work item operation.");
        }
    }

    private void AddAudit(Guid organizationId, Guid actorUserId, string action, Guid workItemId, string? detailsJson) =>
        dbContext.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            EntityType = "WorkItem",
            EntityId = workItemId,
            DetailsJson = detailsJson,
            OccurredAtUtc = DateTime.UtcNow
        });

    private static IOrderedQueryable<WorkItem> ApplyOrdering(IQueryable<WorkItem> query, string sortBy, string sortDirection)
    {
        var descending = sortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);
        if (!descending && !sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("sortDirection must be 'asc' or 'desc'.");
        }

        var ordered = (sortBy.ToLowerInvariant(), descending) switch
        {
            ("createdatutc", false) => query.OrderBy(item => item.CreatedAtUtc),
            ("createdatutc", true) => query.OrderByDescending(item => item.CreatedAtUtc),
            ("updatedatutc", false) => query.OrderBy(item => item.UpdatedAtUtc),
            ("updatedatutc", true) => query.OrderByDescending(item => item.UpdatedAtUtc),
            ("dueatutc", false) => query.OrderBy(item => item.DueAtUtc),
            ("dueatutc", true) => query.OrderByDescending(item => item.DueAtUtc),
            ("priority", false) => query.OrderBy(item => item.Priority == WorkItemPriority.Low ? 0
                : item.Priority == WorkItemPriority.Medium ? 1 : item.Priority == WorkItemPriority.High ? 2 : 3),
            ("priority", true) => query.OrderByDescending(item => item.Priority == WorkItemPriority.Low ? 0
                : item.Priority == WorkItemPriority.Medium ? 1 : item.Priority == WorkItemPriority.High ? 2 : 3),
            ("status", false) => query.OrderBy(item => item.Status == WorkItemStatus.New ? 0
                : item.Status == WorkItemStatus.Triaged ? 1 : item.Status == WorkItemStatus.Assigned ? 2
                : item.Status == WorkItemStatus.InProgress ? 3 : item.Status == WorkItemStatus.CodeReview ? 4
                : item.Status == WorkItemStatus.QA ? 5 : item.Status == WorkItemStatus.Resolved ? 6 : 7),
            ("status", true) => query.OrderByDescending(item => item.Status == WorkItemStatus.New ? 0
                : item.Status == WorkItemStatus.Triaged ? 1 : item.Status == WorkItemStatus.Assigned ? 2
                : item.Status == WorkItemStatus.InProgress ? 3 : item.Status == WorkItemStatus.CodeReview ? 4
                : item.Status == WorkItemStatus.QA ? 5 : item.Status == WorkItemStatus.Resolved ? 6 : 7),
            ("title", false) => query.OrderBy(item => item.Title),
            ("title", true) => query.OrderByDescending(item => item.Title),
            _ => throw new ArgumentException("sortBy must be createdAtUtc, updatedAtUtc, dueAtUtc, priority, status, or title.")
        };
        return ordered.ThenBy(item => item.Id);
    }

    private static T ParseEnum<T>(string value, string fieldName) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{fieldName} is required.");
        }
        var name = Enum.GetNames<T>().FirstOrDefault(candidate =>
            candidate.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        return name is null
            ? throw new ArgumentException($"{fieldName} must be one of: {string.Join(", ", Enum.GetNames<T>())}.")
            : Enum.Parse<T>(name);
    }

    private static void ValidateDetails(string title, string? description)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length is < 2 or > MaximumTitleLength)
        {
            throw new ArgumentException($"Title must be 2-{MaximumTitleLength} characters.");
        }
        if (description?.Length > MaximumDescriptionLength)
        {
            throw new ArgumentException($"Description must be {MaximumDescriptionLength} characters or fewer.");
        }
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();

    private static DateTime? NormalizeUtc(DateTimeOffset? value, string fieldName) =>
        value.HasValue ? value.Value.UtcDateTime : null;

    private static int GetOffset(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > MaximumPageSize || (long)(page - 1) * pageSize > int.MaxValue)
        {
            throw new ArgumentException($"Page must be positive, pageSize must be between 1 and {MaximumPageSize}, and the offset must be supported.");
        }
        return (page - 1) * pageSize;
    }
}
