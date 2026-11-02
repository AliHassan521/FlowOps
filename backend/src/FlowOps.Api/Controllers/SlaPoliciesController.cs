using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Sla;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowOps.Api.Controllers;

[ApiController, Authorize, Route("api/organizations/{organizationId:guid}/sla-policies")]
public sealed class SlaPoliciesController(ISlaService sla) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50, [FromQuery] bool activeOnly = true, CancellationToken cancellationToken = default) =>
        Ok(await sla.ListPoliciesAsync(UserId, organizationId, page, pageSize, activeOnly, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(Guid organizationId, CreateSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        var policy = await sla.CreatePolicyAsync(UserId, organizationId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, policy);
    }

    [HttpDelete("{policyId:guid}")]
    public async Task<IActionResult> Archive(Guid organizationId, Guid policyId, CancellationToken cancellationToken)
    {
        await sla.ArchivePolicyAsync(UserId, organizationId, policyId, cancellationToken);
        return NoContent();
    }

    [HttpGet("~/api/organizations/{organizationId:guid}/sla-summary")]
    public async Task<IActionResult> Summary(Guid organizationId, CancellationToken cancellationToken) =>
        Ok(await sla.SummaryAsync(UserId, organizationId, cancellationToken));

    private Guid UserId => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new UnauthorizedAccessException("A valid user identity is required.");
}
