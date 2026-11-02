using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class WorkItemSlaConfiguration : IEntityTypeConfiguration<WorkItemSla>
{
    public void Configure(EntityTypeBuilder<WorkItemSla> builder)
    {
        builder.ToTable("WorkItemSlas", table =>
        {
            table.HasCheckConstraint("CK_WorkItemSlas_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
            table.HasCheckConstraint("CK_WorkItemSlas_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
            table.HasCheckConstraint("CK_WorkItemSlas_Deadline", "[DeadlineAtUtc] > [StartedAtUtc]");
        });
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Property(s => s.PolicyName).HasMaxLength(120).IsRequired();
        builder.HasIndex(s => s.WorkItemId).IsUnique();
        builder.HasIndex(s => new { s.CompletedAtUtc, s.BreachedAtUtc, s.DeadlineAtUtc });
        builder.HasIndex(s => new { s.CompletedAtUtc, s.WarnedAtUtc, s.WarningAtUtc });
        builder.HasOne(s => s.WorkItem).WithOne().HasForeignKey<WorkItemSla>(s => s.WorkItemId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Policy).WithMany().HasForeignKey(s => s.PolicyId).OnDelete(DeleteBehavior.Restrict);
    }
}
