using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Projects;

public sealed record UpdateProjectRequest(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [StringLength(1000)] string? Description);
