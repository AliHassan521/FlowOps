using FlowOps.Domain.Enums;
namespace FlowOps.Domain.Incidents;

public static class IncidentWorkflow
{
    public static bool CanTransition(IncidentStatus current, IncidentStatus next) => (current, next) is
        (IncidentStatus.Open, IncidentStatus.Investigating) or
        (IncidentStatus.Investigating, IncidentStatus.Mitigated) or
        (IncidentStatus.Mitigated, IncidentStatus.Resolved) or
        (IncidentStatus.Resolved, IncidentStatus.Closed);
}
