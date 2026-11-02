using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class SlaPolicyConfiguration : IEntityTypeConfiguration<SlaPolicy>
{
    public void Configure(EntityTypeBuilder<SlaPolicy> builder)
    {
        builder.ToTable("SlaPolicies", table =>
        {
            table.HasCheckConstraint("CK_SlaPolicies_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
            table.HasCheckConstraint("CK_SlaPolicies_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
        });
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(120).IsRequired();
        builder.Property(p => p.NormalizedName).HasMaxLength(120).IsRequired();
        builder.Property(p => p.IsActive).IsConcurrencyToken();
        builder.HasIndex(p => new { p.OrganizationId, p.NormalizedName }).IsUnique();
        builder.HasOne(p => p.Organization).WithMany().HasForeignKey(p => p.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
