using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Organizations;

public sealed record UpdateOrganizationMemberRoleRequest(
    [Required, RegularExpression("(?i)^(Admin|TeamLead|Engineer|QA|Viewer)$")] string Role);
