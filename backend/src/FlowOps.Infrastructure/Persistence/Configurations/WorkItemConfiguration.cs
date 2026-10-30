using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.ToTable("WorkItems");
        builder.HasKey(workItem => workItem.Id);

        builder.Property(workItem => workItem.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(workItem => workItem.Title).HasMaxLength(200).IsRequired();
        builder.Property(workItem => workItem.Description).HasMaxLength(8000);
        builder.Property(workItem => workItem.Priority).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(workItem => workItem.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(workItem => workItem.CreatedAtUtc).IsRequired();
        builder.Property(workItem => workItem.UpdatedAtUtc).IsRequired();
        builder.HasIndex(workItem => new { workItem.ProjectId, workItem.Status, workItem.CreatedAtUtc });
        builder.HasIndex(workItem => new { workItem.ProjectId, workItem.AssigneeUserId, workItem.Status });
        builder.HasIndex(workItem => new { workItem.ProjectId, workItem.Priority, workItem.DueAtUtc });

        builder.HasOne(workItem => workItem.Project)
            .WithMany(project => project.WorkItems)
            .HasForeignKey(workItem => workItem.ProjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(workItem => workItem.Reporter)
            .WithMany()
            .HasForeignKey(workItem => workItem.ReporterUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(workItem => workItem.Assignee)
            .WithMany()
            .HasForeignKey(workItem => workItem.AssigneeUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
