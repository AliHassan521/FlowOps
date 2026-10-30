using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");
        builder.HasKey(auditEvent => auditEvent.Id);

        builder.Property(auditEvent => auditEvent.Action).IsRequired().HasMaxLength(80).IsUnicode(false);
        builder.Property(auditEvent => auditEvent.EntityType).IsRequired().HasMaxLength(80).IsUnicode(false);
        builder.Property(auditEvent => auditEvent.DetailsJson).HasMaxLength(2000);
        builder.Property(auditEvent => auditEvent.OccurredAtUtc).IsRequired();
        builder.HasIndex(auditEvent => new { auditEvent.OrganizationId, auditEvent.OccurredAtUtc });
        builder.HasIndex(auditEvent => new { auditEvent.EntityType, auditEvent.EntityId, auditEvent.OccurredAtUtc });

        builder.HasOne(auditEvent => auditEvent.Organization)
            .WithMany(organization => organization.AuditEvents)
            .HasForeignKey(auditEvent => auditEvent.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(auditEvent => auditEvent.Actor)
            .WithMany()
            .HasForeignKey(auditEvent => auditEvent.ActorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
