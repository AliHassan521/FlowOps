using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.WorkItems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOps.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/work-items")]
public sealed class WorkItemsController(IWorkItemService workItemService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(WorkItemResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid organizationId, Guid projectId, CreateWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        var workItem = await workItemService.CreateAsync(CurrentUserId, organizationId, projectId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { organizationId, projectId, workItemId = workItem.Id }, workItem);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<WorkItemResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<WorkItemResponse>>> List(Guid organizationId, Guid projectId,
        [FromQuery] WorkItemListQuery query, CancellationToken cancellationToken) =>
        Ok(await workItemService.ListAsync(CurrentUserId, organizationId, projectId, query, cancellationToken));

    [HttpGet("{workItemId:guid}")]
    [ProducesResponseType(typeof(WorkItemResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkItemResponse>> Get(Guid organizationId, Guid projectId, Guid workItemId,
        CancellationToken cancellationToken) =>
        Ok(await workItemService.GetAsync(CurrentUserId, organizationId, projectId, workItemId, cancellationToken));

    [HttpPut("{workItemId:guid}")]
    [ProducesResponseType(typeof(WorkItemResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkItemResponse>> Update(Guid organizationId, Guid projectId, Guid workItemId,
        UpdateWorkItemRequest request, CancellationToken cancellationToken) =>
        Ok(await workItemService.UpdateAsync(CurrentUserId, organizationId, projectId, workItemId, request, cancellationToken));

    [HttpPut("{workItemId:guid}/assignee")]
    [ProducesResponseType(typeof(WorkItemResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkItemResponse>> Assign(Guid organizationId, Guid projectId, Guid workItemId,
        AssignWorkItemRequest request, CancellationToken cancellationToken) =>
        Ok(await workItemService.AssignAsync(CurrentUserId, organizationId, projectId, workItemId, request, cancellationToken));

    [HttpPost("{workItemId:guid}/transitions")]
    [ProducesResponseType(typeof(WorkItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WorkItemResponse>> Transition(Guid organizationId, Guid projectId, Guid workItemId,
        TransitionWorkItemRequest request, CancellationToken cancellationToken) =>
        Ok(await workItemService.TransitionAsync(CurrentUserId, organizationId, projectId, workItemId, request, cancellationToken));

    [HttpGet("{workItemId:guid}/comments")]
    [ProducesResponseType(typeof(PagedResponse<WorkItemCommentResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<WorkItemCommentResponse>>> ListComments(Guid organizationId, Guid projectId,
        Guid workItemId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await workItemService.ListCommentsAsync(CurrentUserId, organizationId, projectId, workItemId, page, pageSize, cancellationToken));

    [HttpPost("{workItemId:guid}/comments")]
    [ProducesResponseType(typeof(WorkItemCommentResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<WorkItemCommentResponse>> AddComment(Guid organizationId, Guid projectId, Guid workItemId,
        AddWorkItemCommentRequest request, CancellationToken cancellationToken)
    {
        var comment = await workItemService.AddCommentAsync(CurrentUserId, organizationId, projectId, workItemId, request, cancellationToken);
        return CreatedAtAction(nameof(ListComments), new { organizationId, projectId, workItemId }, comment);
    }

    [HttpGet("{workItemId:guid}/history")]
    [ProducesResponseType(typeof(PagedResponse<WorkItemHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<WorkItemHistoryResponse>>> ListHistory(Guid organizationId, Guid projectId,
        Guid workItemId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await workItemService.ListHistoryAsync(CurrentUserId, organizationId, projectId, workItemId, page, pageSize, cancellationToken));

    private Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedAccessException("The authenticated token does not contain a valid user ID.");
}
