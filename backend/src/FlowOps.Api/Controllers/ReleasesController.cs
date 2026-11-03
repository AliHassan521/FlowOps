using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Releases;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FlowOps.Api.Controllers;

[ApiController, Authorize, RequestSizeLimit(65536), Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/releases")]
public sealed class ReleasesController(IReleaseService service) : ControllerBase
{
    [HttpGet("environments")]
    public async Task<IActionResult> Environments(Guid organizationId, Guid projectId, int page = 1, int pageSize = 50, bool activeOnly = true, CancellationToken ct = default) => Ok(await service.EnvironmentsAsync(UserId, organizationId, projectId, page, pageSize, activeOnly, ct));
    [HttpPost("environments")]
    public async Task<IActionResult> CreateEnvironment(Guid organizationId, Guid projectId, CreateEnvironmentRequest request, CancellationToken ct) => StatusCode(201, await service.CreateEnvironmentAsync(UserId, organizationId, projectId, request, ct));
    [HttpDelete("environments/{environmentId:guid}")]
    public async Task<IActionResult> Archive(Guid organizationId, Guid projectId, Guid environmentId, CancellationToken ct) { await service.ArchiveEnvironmentAsync(UserId, organizationId, projectId, environmentId, ct); return NoContent(); }
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, Guid projectId, [FromQuery] ReleaseListQuery query, CancellationToken ct) => Ok(await service.ListAsync(UserId, organizationId, projectId, query, ct));
    [HttpPost]
    public async Task<IActionResult> Create(Guid organizationId, Guid projectId, CreateReleaseRequest request, CancellationToken ct) { var release = await service.CreateAsync(UserId, organizationId, projectId, request, ct); return CreatedAtAction(nameof(Get), new { organizationId, projectId, releaseId = release.Id }, release); }
    [HttpGet("{releaseId:guid}")]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid releaseId, CancellationToken ct) => Ok(await service.GetAsync(UserId, organizationId, projectId, releaseId, ct));
    [HttpPut("{releaseId:guid}")]
    public async Task<IActionResult> Update(Guid organizationId, Guid projectId, Guid releaseId, UpdateReleaseRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(UserId, organizationId, projectId, releaseId, request, ct));
    [HttpGet("{releaseId:guid}/links")]
    public async Task<IActionResult> Links(Guid organizationId, Guid projectId, Guid releaseId, CancellationToken ct) => Ok(await service.LinksAsync(UserId, organizationId, projectId, releaseId, ct));
    [HttpPut("{releaseId:guid}/links")]
    public async Task<IActionResult> SetLinks(Guid organizationId, Guid projectId, Guid releaseId, SetReleaseLinksRequest request, CancellationToken ct) => Ok(await service.SetLinksAsync(UserId, organizationId, projectId, releaseId, request, ct));
    [HttpPost("{releaseId:guid}/transitions")]
    public async Task<IActionResult> Transition(Guid organizationId, Guid projectId, Guid releaseId, TransitionReleaseRequest request, CancellationToken ct) => Ok(await service.TransitionAsync(UserId, organizationId, projectId, releaseId, request, ct));
    [HttpGet("{releaseId:guid}/timeline")]
    public async Task<IActionResult> Timeline(Guid organizationId, Guid projectId, Guid releaseId, int page = 1, int pageSize = 50, CancellationToken ct = default) => Ok(await service.TimelineAsync(UserId, organizationId, projectId, releaseId, page, pageSize, ct));
    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("A valid identity is required.");
}
