using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> b)
    {
        b.ToTable("Incidents", table =>
        {
            table.HasCheckConstraint("CK_Incidents_Severity", "[Severity] IN ('Low','Medium','High','Critical')");
            table.HasCheckConstraint("CK_Incidents_Status", "[Status] IN ('Open','Investigating','Mitigated','Resolved','Closed')");
            table.HasCheckConstraint("CK_Incidents_Resolution", "([Status] NOT IN ('Resolved','Closed') AND [EndedAtUtc] IS NULL) OR ([Status] IN ('Resolved','Closed') AND [EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc] AND [Resolution] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL)");
            table.HasCheckConstraint("CK_Incidents_Owner", "[Status] NOT IN ('Investigating','Mitigated') OR [OwnerUserId] IS NOT NULL");
        });
        b.HasKey(i => i.Id); b.Ignore(i => i.Number); b.Property(i => i.Version).IsConcurrencyToken();
        b.Property(i => i.Title).HasMaxLength(200).IsRequired(); b.Property(i => i.Description).HasMaxLength(8000);
        b.Property(i => i.AffectedService).HasMaxLength(120).IsRequired();
        b.Property(i => i.Severity).HasConversion<string>().HasMaxLength(16); b.Property(i => i.Status).HasConversion<string>().HasMaxLength(24);
        b.Property(i => i.Resolution).HasMaxLength(4000); b.Property(i => i.RootCause).HasMaxLength(4000);
        b.HasOne(i => i.Project).WithMany().HasForeignKey(i => i.ProjectId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(i => i.Reporter).WithMany().HasForeignKey(i => i.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(i => i.Owner).WithMany().HasForeignKey(i => i.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(i => i.ResolvedBy).WithMany().HasForeignKey(i => i.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(i => new { i.ProjectId, i.Status, i.StartedAtUtc, i.Id });
        b.HasIndex(i => new { i.ProjectId, i.Severity }); b.HasIndex(i => i.OwnerUserId);
    }
}
public sealed class IncidentCommentConfiguration : IEntityTypeConfiguration<IncidentComment>
{
    public void Configure(EntityTypeBuilder<IncidentComment> b)
    {
        b.HasKey(c => c.Id); b.Property(c => c.Body).HasMaxLength(4000).IsRequired();
        b.HasOne(c => c.Incident).WithMany().HasForeignKey(c => c.IncidentId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => new { c.IncidentId, c.CreatedAtUtc, c.Id });
    }
}
