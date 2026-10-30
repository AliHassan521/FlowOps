using System.ComponentModel.DataAnnotations;

namespace FlowOps.Contracts.Organizations;

public sealed record AddOrganizationMemberRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, RegularExpression("(?i)^(Admin|TeamLead|Engineer|QA|Viewer)$")] string Role);
