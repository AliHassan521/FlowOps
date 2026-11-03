using System.Security.Claims;
using FlowOps.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FlowOps.Api.Controllers;

[ApiController, Authorize, Route("api/organizations/{organizationId:guid}/dashboard")]
public sealed class DashboardController(IDashboardService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, Guid? projectId = null, int periodDays = 30, int workloadPage = 1, CancellationToken ct = default)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) throw new UnauthorizedAccessException("A valid identity is required.");
        return Ok(await service.GetAsync(actor, organizationId, projectId, periodDays, workloadPage, ct));
    }
}
