using FlowOps.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Explicit one-shot migration tool. The API never migrates a production database on startup.
var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("Set ConnectionStrings__DefaultConnection for the reviewed migration target.");
    return 1;
}

try
{
    var connection = new SqlConnectionStringBuilder(connectionString);
    if (string.IsNullOrWhiteSpace(connection.InitialCatalog) || connection.InitialCatalog.Equals("master", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("A dedicated application database is required.");

    var runtimePassword = Environment.GetEnvironmentVariable("FLOWOPS_LOCAL_RUNTIME_PASSWORD");
    if (runtimePassword is not null &&
        (Environment.GetEnvironmentVariable("FLOWOPS_LOCAL_PROVISION") != "true"
         || connection.InitialCatalog != "FlowOps"
         || connection.DataSource != "sql,1433"
         || runtimePassword.Length is < 32 or > 128))
        throw new InvalidOperationException("Runtime login provisioning is restricted to the documented local Compose database.");

    var options = new DbContextOptionsBuilder<FlowOpsDbContext>()
        .UseSqlServer(connectionString, sql => sql.CommandTimeout(120)).Options;
    await using var db = new FlowOpsDbContext(options);
    await db.Database.MigrateAsync();

    if (runtimePassword is not null)
    {
        // SQL parameters carry the secret; QUOTENAME safely constructs DDL requiring a literal.
        // Runtime credentials are distinct from the migration login and have no DDL/server role.
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @password nvarchar(128) = {runtimePassword};
            DECLARE @sql nvarchar(max);
            IF SUSER_ID(N'FlowOpsRuntime') IS NULL
                SET @sql = N'CREATE LOGIN [FlowOpsRuntime] WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON';
            ELSE
                SET @sql = N'ALTER LOGIN [FlowOpsRuntime] WITH PASSWORD = ' + QUOTENAME(@password, '''');
            EXEC sp_executesql @sql;
            IF USER_ID(N'FlowOpsRuntime') IS NULL CREATE USER [FlowOpsRuntime] FOR LOGIN [FlowOpsRuntime];
            ALTER ROLE [db_datareader] ADD MEMBER [FlowOpsRuntime];
            ALTER ROLE [db_datawriter] ADD MEMBER [FlowOpsRuntime];
            DENY UPDATE, DELETE ON OBJECT::dbo.AuditEvents TO [FlowOpsRuntime];
            DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.__EFMigrationsHistory TO [FlowOpsRuntime];
            """);
    }
    Console.WriteLine("Reviewed database migrations applied successfully.");
    return 0;
}
catch (Exception exception)
{
    // Connection strings, DDL passwords and SQL payloads must never reach console logs.
    Console.Error.WriteLine($"Migration failed ({exception.GetType().Name}). Check target configuration, database access and reviewed migration SQL.");
    return 1;
}
