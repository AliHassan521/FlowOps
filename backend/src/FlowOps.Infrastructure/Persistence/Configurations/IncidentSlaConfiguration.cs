using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class IncidentSlaConfiguration : IEntityTypeConfiguration<IncidentSla>
{
    public void Configure(EntityTypeBuilder<IncidentSla> builder)
    {
        builder.ToTable("IncidentSlas", table =>
        {
            table.HasCheckConstraint("CK_IncidentSlas_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
            table.HasCheckConstraint("CK_IncidentSlas_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
            table.HasCheckConstraint("CK_IncidentSlas_Deadline", "[DeadlineAtUtc] > [StartedAtUtc]");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Property(s => s.PolicyName).HasMaxLength(120).IsRequired();
        builder.HasIndex(s => s.IncidentId).IsUnique();
        builder.HasIndex(s => new { s.CompletedAtUtc, s.BreachedAtUtc, s.DeadlineAtUtc });
        builder.HasIndex(s => new { s.CompletedAtUtc, s.WarnedAtUtc, s.WarningAtUtc });
        builder.HasOne(s => s.Incident).WithOne().HasForeignKey<IncidentSla>(s => s.IncidentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Policy).WithMany().HasForeignKey(s => s.PolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}
