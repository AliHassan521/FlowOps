using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Common;
using FlowOps.Contracts.Projects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOps.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/organizations/{organizationId:guid}/projects")]
public sealed class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid organizationId, CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await projectService.CreateAsync(CurrentUserId, organizationId, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { organizationId, projectId = project.Id }, project);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ProjectResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProjectResponse>>> List(Guid organizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        Ok(await projectService.ListAsync(CurrentUserId, organizationId, page, pageSize, cancellationToken));

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Get(Guid organizationId, Guid projectId, CancellationToken cancellationToken) =>
        Ok(await projectService.GetAsync(CurrentUserId, organizationId, projectId, cancellationToken));

    [HttpPut("{projectId:guid}")]
    [ProducesResponseType(typeof(ProjectResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ProjectResponse>> Update(Guid organizationId, Guid projectId, UpdateProjectRequest request,
        CancellationToken cancellationToken) =>
        Ok(await projectService.UpdateAsync(CurrentUserId, organizationId, projectId, request, cancellationToken));

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid organizationId, Guid projectId, CancellationToken cancellationToken)
    {
        await projectService.DeactivateAsync(CurrentUserId, organizationId, projectId, cancellationToken);
        return NoContent();
    }

    private Guid CurrentUserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedAccessException("The authenticated token does not contain a valid user ID.");
}
