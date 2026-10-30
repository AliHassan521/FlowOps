using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Name).IsRequired().HasMaxLength(120);
        builder.Property(project => project.Key).IsRequired().HasMaxLength(10).IsUnicode(false);
        builder.Property(project => project.Description).HasMaxLength(1000);
        builder.Property(project => project.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(project => project.CreatedAtUtc).IsRequired();
        builder.HasIndex(project => new { project.OrganizationId, project.Key }).IsUnique();
        builder.HasIndex(project => new { project.OrganizationId, project.IsActive, project.Name });

        builder.HasOne(project => project.Organization)
            .WithMany()
            .HasForeignKey(project => project.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
