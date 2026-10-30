using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships");
        builder.HasKey(membership => new { membership.OrganizationId, membership.UserId });

        builder.Property(membership => membership.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(membership => membership.JoinedAtUtc).IsRequired();
        builder.HasIndex(membership => new { membership.UserId, membership.IsActive });
        builder.HasIndex(membership => new { membership.OrganizationId, membership.RoleId, membership.IsActive });

        builder.HasOne(membership => membership.Organization)
            .WithMany(organization => organization.Memberships)
            .HasForeignKey(membership => membership.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(membership => membership.User)
            .WithMany()
            .HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(membership => membership.Role)
            .WithMany()
            .HasForeignKey(membership => membership.RoleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
