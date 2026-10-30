using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Organizations;

public sealed record UpdateOrganizationRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description);
