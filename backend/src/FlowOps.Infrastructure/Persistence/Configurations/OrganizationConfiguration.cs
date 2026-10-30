using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(organization => organization.Id);

        builder.Property(organization => organization.Name)
            .IsRequired()
            .HasMaxLength(120);

        builder.Property(organization => organization.Slug)
            .IsRequired()
            .HasMaxLength(80)
            .IsUnicode(false);

        builder.HasIndex(organization => organization.Slug).IsUnique();
        builder.Property(organization => organization.Description).HasMaxLength(500);
        builder.Property(organization => organization.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(organization => organization.CreatedAtUtc).IsRequired();
    }
}
