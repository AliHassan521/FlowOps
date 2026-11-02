using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Incidents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FlowOps.Api.Controllers;

[ApiController, Authorize, RequestSizeLimit(65536), Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/incidents")]
public sealed class IncidentsController(IIncidentService incidents) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid organizationId, Guid projectId, CreateIncidentRequest request, CancellationToken ct)
    {
        var item = await incidents.CreateAsync(UserId, organizationId, projectId, request, ct);
        return CreatedAtAction(nameof(Get), new { organizationId, projectId, incidentId = item.Id }, item);
    }
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, Guid projectId, [FromQuery] IncidentListQuery query, CancellationToken ct) =>
        Ok(await incidents.ListAsync(UserId, organizationId, projectId, query, ct));
    [HttpGet("{incidentId:guid}")]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid incidentId, CancellationToken ct) =>
        Ok(await incidents.GetAsync(UserId, organizationId, projectId, incidentId, ct));
    [HttpPut("{incidentId:guid}")]
    public async Task<IActionResult> Update(Guid organizationId, Guid projectId, Guid incidentId, UpdateIncidentRequest request, CancellationToken ct) =>
        Ok(await incidents.UpdateAsync(UserId, organizationId, projectId, incidentId, request, ct));
    [HttpPut("{incidentId:guid}/owner")]
    public async Task<IActionResult> Assign(Guid organizationId, Guid projectId, Guid incidentId, AssignIncidentRequest request, CancellationToken ct) =>
        Ok(await incidents.AssignAsync(UserId, organizationId, projectId, incidentId, request, ct));
    [HttpPost("{incidentId:guid}/transitions")]
    public async Task<IActionResult> Transition(Guid organizationId, Guid projectId, Guid incidentId, TransitionIncidentRequest request, CancellationToken ct) =>
        Ok(await incidents.TransitionAsync(UserId, organizationId, projectId, incidentId, request, ct));
    [HttpPost("{incidentId:guid}/comments")]
    public async Task<IActionResult> Comment(Guid organizationId, Guid projectId, Guid incidentId, AddIncidentCommentRequest request, CancellationToken ct) =>
        StatusCode(201, await incidents.CommentAsync(UserId, organizationId, projectId, incidentId, request, ct));
    [HttpGet("{incidentId:guid}/comments")]
    public async Task<IActionResult> Comments(Guid organizationId, Guid projectId, Guid incidentId, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        Ok(await incidents.CommentsAsync(UserId, organizationId, projectId, incidentId, page, pageSize, ct));
    [HttpGet("{incidentId:guid}/timeline")]
    public async Task<IActionResult> Timeline(Guid organizationId, Guid projectId, Guid incidentId, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        Ok(await incidents.TimelineAsync(UserId, organizationId, projectId, incidentId, page, pageSize, ct));
    [HttpGet("{incidentId:guid}/sla")]
    public async Task<IActionResult> Sla(Guid organizationId, Guid projectId, Guid incidentId, CancellationToken ct) =>
        Ok(await incidents.SlaAsync(UserId, organizationId, projectId, incidentId, ct));
    [HttpPost("{incidentId:guid}/sla")]
    public async Task<IActionResult> Attach(Guid organizationId, Guid projectId, Guid incidentId, AttachIncidentSlaRequest request, CancellationToken ct) =>
        StatusCode(201, await incidents.AttachSlaAsync(UserId, organizationId, projectId, incidentId, request, ct));
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(Guid organizationId, Guid projectId, CancellationToken ct) =>
        Ok(await incidents.SummaryAsync(UserId, organizationId, projectId, ct));
    [HttpGet("~/api/organizations/{organizationId:guid}/incident-summary")]
    public async Task<IActionResult> OrganizationSummary(Guid organizationId, CancellationToken ct) =>
        Ok(await incidents.SummaryAsync(UserId, organizationId, null, ct));
    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("A valid identity is required.");
}
