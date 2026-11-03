using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class ReleaseConfiguration : IEntityTypeConfiguration<Release>
{
    public void Configure(EntityTypeBuilder<Release> b)
    {
        b.ToTable("Releases", table =>
        {
            table.HasCheckConstraint("CK_Releases_Status", "[Status] IN ('Draft','Approved','Deploying','Successful','Failed')");
            table.HasCheckConstraint("CK_Releases_Approval", "[Status] = 'Draft' OR ([ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] >= [CreatedAtUtc])");
            table.HasCheckConstraint("CK_Releases_Started", "[Status] NOT IN ('Deploying','Successful','Failed') OR ([DeploymentStartedAtUtc] IS NOT NULL AND [DeploymentStartedAtUtc] >= [ApprovedAtUtc])");
            table.HasCheckConstraint("CK_Releases_Completed", "([Status] IN ('Successful','Failed') AND [CompletedAtUtc] IS NOT NULL AND [Outcome] IS NOT NULL AND [CompletedAtUtc] >= [DeploymentStartedAtUtc]) OR ([Status] NOT IN ('Successful','Failed') AND [CompletedAtUtc] IS NULL)");
        });
        b.HasKey(r => r.Id); b.Property(r => r.Version).IsConcurrencyToken();
        b.Property(r => r.ApplicationName).HasMaxLength(120).IsRequired(); b.Property(r => r.ReleaseVersion).HasMaxLength(80).IsRequired();
        b.Property(r => r.Notes).HasMaxLength(8000); b.Property(r => r.Outcome).HasMaxLength(4000);
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(24);
        b.HasOne(r => r.Project).WithMany().HasForeignKey(r => r.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.Environment).WithMany().HasForeignKey(r => r.EnvironmentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.CreatedBy).WithMany().HasForeignKey(r => r.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(r => r.ApprovedBy).WithMany().HasForeignKey(r => r.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(r => new { r.ProjectId, r.CreatedAtUtc, r.Id }); b.HasIndex(r => new { r.ProjectId, r.Status });
    }
}
public sealed class ReleaseEnvironmentConfiguration : IEntityTypeConfiguration<ReleaseEnvironment>
{
    public void Configure(EntityTypeBuilder<ReleaseEnvironment> b)
    {
        b.HasKey(e => e.Id); b.Property(e => e.Name).HasMaxLength(80).IsRequired(); b.Property(e => e.NormalizedName).HasMaxLength(80).IsRequired();
        b.HasIndex(e => new { e.ProjectId, e.NormalizedName }).IsUnique();
        b.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ReleaseWorkItemConfiguration : IEntityTypeConfiguration<ReleaseWorkItem>
{
    public void Configure(EntityTypeBuilder<ReleaseWorkItem> b)
    {
        b.HasKey(l => new { l.ReleaseId, l.WorkItemId });
        b.HasOne(l => l.Release).WithMany(r => r.WorkItems).HasForeignKey(l => l.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(l => l.WorkItem).WithMany().HasForeignKey(l => l.WorkItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class ReleaseIncidentConfiguration : IEntityTypeConfiguration<ReleaseIncident>
{
    public void Configure(EntityTypeBuilder<ReleaseIncident> b)
    {
        b.HasKey(l => new { l.ReleaseId, l.IncidentId });
        b.HasOne(l => l.Release).WithMany(r => r.Incidents).HasForeignKey(l => l.ReleaseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(l => l.Incident).WithMany().HasForeignKey(l => l.IncidentId).OnDelete(DeleteBehavior.Restrict);
    }
}
