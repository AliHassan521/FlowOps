using FlowOps.Api.Middleware;
using FlowOps.Application.Common.Configuration;
using FlowOps.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// ── Services ──────────────────────────────────────────────────────────────
// Register EF Core + SQL Server (defined in FlowOps.Infrastructure)
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOptions<JwtSettings>()
    .BindConfiguration(JwtSettings.SectionName)
    .Validate(settings => settings.SecretKey.Length >= 32, "JwtSettings:SecretKey must contain at least 32 characters.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer), "JwtSettings:Issuer is required.")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Audience), "JwtSettings:Audience is required.")
    .Validate(settings => settings.AccessTokenMinutes is >= 5 and <= 60, "JwtSettings:AccessTokenMinutes must be between 5 and 60.")
    .Validate(settings => settings.RefreshTokenDays is >= 1 and <= 90, "JwtSettings:RefreshTokenDays must be between 1 and 90.")
    .ValidateOnStart();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (allowedOrigins.Any(origin =>
        !Uri.TryCreate(origin, UriKind.Absolute, out var parsedOrigin)
        || parsedOrigin.Scheme is not ("http" or "https")
        || !string.Equals(origin, parsedOrigin.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal)))
{
    throw new InvalidOperationException("Cors:AllowedOrigins must contain only exact HTTP or HTTPS origins.");
}

if (!builder.Environment.IsDevelopment()
    && (allowedOrigins.Length == 0 || allowedOrigins.Any(origin => !origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))))
{
    throw new InvalidOperationException("Production requires at least one HTTPS origin in Cors:AllowedOrigins.");
}

builder.Services.AddCors(options => options.AddPolicy("frontend", policy =>
{
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// Swagger / OpenAPI documentation with JWT Bearer authentication support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.OpenApiInfo
    {
        Title = "FlowOps API",
        Version = "v1",
        Description = "Engineering operations & incident management platform API"
    });

    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.ParameterLocation.Header,
        Description = "Enter your JWT token directly (e.g. eyJhbGci...)"
    });

    options.AddSecurityRequirement(_ => new Microsoft.OpenApi.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.OpenApiSecuritySchemeReference("Bearer"),
            new List<string>()
        }
    });
});

// Health checks: ASP.NET built-in system.
// AddSqlServer verifies the DB is reachable when /health is called.
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: builder.Configuration.GetConnectionString("DefaultConnection")!,
        name:             "sql-server",
        tags:             ["database", "ready"]);

// ── JWT Authentication ────────────────────────────────────────────────────
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
    ?? throw new InvalidOperationException("JwtSettings configuration is required.");

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme    = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, settingsOptions) =>
    {
        var jwtSettings = settingsOptions.Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSettings.Issuer,
            ValidAudience            = jwtSettings.Audience,
            IssuerSigningKey         = new SymmetricSecurityKey(
                                          System.Text.Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew                = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OrganizationAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("TeamLeadOrAdmin", policy => policy.RequireRole("Admin", "TeamLead"));
});

// ── Build App ─────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Middleware Pipeline (ORDER MATTERS) ───────────────────────────────────
// 1. Exception handler MUST be first — it wraps everything below it.
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 2. Swagger / OpenAPI documentation UI
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "FlowOps API v1");
        c.RoutePrefix = "swagger";
    });
}

// 3. HTTPS redirect (only in production; in local development it drops auth headers on 307 redirect)
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("frontend");
app.UseRateLimiter();

// 3. Authentication — reads and validates the JWT token from the request header.
//    Must come BEFORE Authorization.
app.UseAuthentication();

// 4. Authorization — checks if the validated user has permission to access the endpoint.
app.UseAuthorization();

// ── Endpoints ─────────────────────────────────────────────────────────────
// Health endpoint with detailed JSON output (shows exact error if Unhealthy).
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name      = e.Key,
                status    = e.Value.Status.ToString(),
                duration  = e.Value.Duration.TotalMilliseconds + "ms"
            })
        };
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
});

// Controller-based API endpoints
app.MapControllers();

app.Run();

// Exposes the entry point to WebApplicationFactory-based API integration tests.
public partial class Program { }
