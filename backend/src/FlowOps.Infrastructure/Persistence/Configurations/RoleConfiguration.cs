using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed GUIDs for consistent seed data across environments
    public static readonly Guid AdminRoleId    = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TeamLeadRoleId = new("22222222-2222-2222-2222-222222222222");
    public static readonly Guid EngineerRoleId = new("33333333-3333-3333-3333-333333333333");
    public static readonly Guid QaRoleId       = new("44444444-4444-4444-4444-444444444444");
    public static readonly Guid ViewerRoleId   = new("55555555-5555-5555-5555-555555555555");

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(r => r.Name)
            .IsUnique();

        builder.Property(r => r.Description)
            .HasMaxLength(250);

        // Seed default organizational roles specified in PRD.md
        builder.HasData(
            new Role { Id = AdminRoleId,    Name = "Admin",    Description = "Organization Administrator with full access" },
            new Role { Id = TeamLeadRoleId, Name = "TeamLead", Description = "Manages projects, assignments, and SLAs" },
            new Role { Id = EngineerRoleId, Name = "Engineer", Description = "Develops tasks, fixes bugs, resolves incidents" },
            new Role { Id = QaRoleId,       Name = "QA",       Description = "Reviews work items and performs verification" },
            new Role { Id = ViewerRoleId,   Name = "Viewer",   Description = "Read-only access to permitted resources" }
        );
    }
}
