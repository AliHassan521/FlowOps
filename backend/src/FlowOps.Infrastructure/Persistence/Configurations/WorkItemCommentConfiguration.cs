using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlowOps.Infrastructure.Persistence.Configurations;

public sealed class WorkItemCommentConfiguration : IEntityTypeConfiguration<WorkItemComment>
{
    public void Configure(EntityTypeBuilder<WorkItemComment> builder)
    {
        builder.ToTable("WorkItemComments");
        builder.HasKey(comment => comment.Id);
        builder.Property(comment => comment.Body).HasMaxLength(4000).IsRequired();
        builder.Property(comment => comment.CreatedAtUtc).IsRequired();
        builder.HasIndex(comment => new { comment.WorkItemId, comment.CreatedAtUtc });

        builder.HasOne(comment => comment.WorkItem)
            .WithMany(workItem => workItem.Comments)
            .HasForeignKey(comment => comment.WorkItemId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(comment => comment.Author)
            .WithMany()
            .HasForeignKey(comment => comment.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
