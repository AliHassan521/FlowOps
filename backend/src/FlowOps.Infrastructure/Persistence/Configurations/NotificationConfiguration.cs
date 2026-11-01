using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Kind).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Message).HasMaxLength(300).IsRequired();
        builder.HasIndex(n => new { n.UserId, n.CreatedAtUtc, n.Id });
        builder.HasIndex(n => new { n.UserId, n.ReadAtUtc });
        builder.HasIndex(n => new { n.EventId, n.UserId }).IsUnique();
        builder.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.Organization).WithMany().HasForeignKey(n => n.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.Project).WithMany().HasForeignKey(n => n.ProjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(n => n.WorkItem).WithMany().HasForeignKey(n => n.WorkItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
