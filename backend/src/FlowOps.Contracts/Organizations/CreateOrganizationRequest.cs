using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Organizations;

public sealed record CreateOrganizationRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [Required, StringLength(80, MinimumLength = 3), RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")] string Slug,
    [StringLength(500)] string? Description);
