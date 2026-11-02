using FlowOps.Application.Common.Interfaces;
using FlowOps.Contracts.Incidents;
using FlowOps.Domain.Entities;
using Xunit;
namespace FlowOps.Application.Tests;
public sealed class ArchitectureTests
{
    [Fact]
    public void Domain_application_and_contracts_do_not_depend_on_delivery_or_persistence_layers()
    {
        foreach (var assembly in new[] { typeof(Incident).Assembly, typeof(IIncidentService).Assembly, typeof(IncidentResponse).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference =>
                reference.Name is "FlowOps.Api" or "FlowOps.Infrastructure"
                || reference.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || reference.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        }
    }
}
