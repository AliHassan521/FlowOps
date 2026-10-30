using FlowOps.Application.Common.Interfaces;
using FlowOps.Infrastructure.Persistence;
using FlowOps.Infrastructure.Security;
using FlowOps.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlowOps.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Database ──────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<FlowOpsDbContext>(options => options.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(FlowOpsDbContext).Assembly.FullName);
        }));

        // ── Infrastructure Services ───────────────────────────────────────
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddScoped<IProjectService, ProjectService>();

        return services;
    }
}
