using FlowOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlowOps.Infrastructure.Persistence;

public class FlowOpsDbContext : DbContext
{
    public FlowOpsDbContext(DbContextOptions<FlowOpsDbContext> options) : base(options) {}

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Project> Projects => Set<Project>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowOpsDbContext).Assembly);
    }
}
