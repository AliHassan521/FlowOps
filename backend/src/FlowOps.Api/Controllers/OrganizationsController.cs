using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Organizations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOps.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations")]
public sealed class OrganizationsController(IOrganizationService organizationService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateOrganizationRequest request, CancellationToken cancellationToken)
    {
        var organization = await organizationService.CreateAsync(CurrentUserId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { organizationId = organization.Id }, organization);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<OrganizationResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrganizationResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await organizationService.ListForUserAsync(CurrentUserId, page, pageSize, cancellationToken));

    [HttpGet("{organizationId:guid}")]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponse>> Get(Guid organizationId, CancellationToken cancellationToken) =>
        Ok(await organizationService.GetAsync(CurrentUserId, organizationId, cancellationToken));

    [HttpPut("{organizationId:guid}")]
    [ProducesResponseType(typeof(OrganizationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrganizationResponse>> Update(
        Guid organizationId,
        UpdateOrganizationRequest request,
        CancellationToken cancellationToken) =>
        Ok(await organizationService.UpdateAsync(CurrentUserId, organizationId, request, cancellationToken));

    [HttpDelete("{organizationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid organizationId, CancellationToken cancellationToken)
    {
        await organizationService.DeactivateAsync(CurrentUserId, organizationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/members")]
    [ProducesResponseType(typeof(PagedResponse<OrganizationMemberResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<OrganizationMemberResponse>>> ListMembers(
        Guid organizationId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await organizationService.ListMembersAsync(CurrentUserId, organizationId, page, pageSize, cancellationToken));

    [HttpPost("{organizationId:guid}/members")]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationMemberResponse>> AddMember(
        Guid organizationId,
        AddOrganizationMemberRequest request,
        CancellationToken cancellationToken) =>
        Ok(await organizationService.AddMemberAsync(CurrentUserId, organizationId, request, cancellationToken));

    [HttpPut("{organizationId:guid}/members/{memberUserId:guid}/role")]
    [ProducesResponseType(typeof(OrganizationMemberResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrganizationMemberResponse>> UpdateMemberRole(
        Guid organizationId,
        Guid memberUserId,
        UpdateOrganizationMemberRoleRequest request,
        CancellationToken cancellationToken) =>
        Ok(await organizationService.UpdateMemberRoleAsync(CurrentUserId, organizationId, memberUserId, request.Role, cancellationToken));

    [HttpDelete("{organizationId:guid}/members/{memberUserId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeactivateMember(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        await organizationService.DeactivateMemberAsync(CurrentUserId, organizationId, memberUserId, cancellationToken);
        return NoContent();
    }

    private Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedAccessException("The authenticated token does not contain a valid user ID.");
}
