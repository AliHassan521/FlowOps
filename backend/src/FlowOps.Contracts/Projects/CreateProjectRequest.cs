using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Projects;

public sealed record CreateProjectRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [Required, StringLength(10, MinimumLength = 2), RegularExpression("^[A-Z][A-Z0-9]*$")] string Key,
    [StringLength(1000)] string? Description);
