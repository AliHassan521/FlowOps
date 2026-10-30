# FlowOps

FlowOps is a modular engineering-operations platform for work tracking, incidents, SLAs, releases, notifications, and audit history.

## Current milestone

Phase 2 authentication code is implemented and verified; phase sign-off is pending:

- BCrypt password hashing
- JWT access tokens held by the Angular client in memory
- Rotating refresh tokens stored only as hashes in SQL Server
- Refresh-token family revocation when a rotated token is replayed
- HttpOnly refresh-token cookie
- Exact configured Origin checks on cookie-backed refresh and logout
- Role-based authorization policies
- Authentication rate limiting
- Angular login, route guard, interceptor, refresh, and logout flow
- API integration tests for the authentication pipeline

The automated API tests use SQLite in memory. The SQL Server migrations were applied
to the configured SQL Server Express database and a fresh LocalDB validation database;
existing password hashes and refresh-token rotation families were preserved. A live
API smoke run against the validation database verified health, registration, protected
profile access, refresh rotation and replay revocation, and logout. The temporary
smoke-test account was removed. The authentication learning check is still pending.

This is an early development milestone, not a deploy-ready product. Organizations,
projects, work items, incidents, releases, SLAs, notifications, Docker, CI/CD, and
deployment are still future phases.

## Local setup

Requirements: .NET 10 SDK, Node.js 24+, Angular CLI, SQL Server Express/local SQL Server.

1. Create/update the database:

   ```powershell
   dotnet ef database update `
     --project backend/src/FlowOps.Infrastructure/FlowOps.Infrastructure.csproj `
     --startup-project backend/src/FlowOps.Api/FlowOps.Api.csproj
   ```

2. Configure the development JWT secret with User Secrets. Do not put it in a committed JSON file:

   ```powershell
   dotnet user-secrets --project backend/src/FlowOps.Api/FlowOps.Api.csproj `
     set JwtSettings:SecretKey "replace-with-a-random-secret-at-least-32-characters"
   ```

3. Start the API:

   ```powershell
   dotnet run --project backend/src/FlowOps.Api/FlowOps.Api.csproj
   ```

4. Start Angular in another terminal:

   ```powershell
   cd frontend
   npm install
   npm start
   ```

The API is available at `http://localhost:5144`; Swagger is at `/swagger`; the Angular app is at `http://localhost:4200`.

## Verification

```powershell
cd backend
dotnet test tests/FlowOps.Application.Tests/FlowOps.Application.Tests.csproj
dotnet build src/FlowOps.Api/FlowOps.Api.csproj --configuration Release

cd ../frontend
npm run build
npm test -- --watch=false
npm audit --audit-level=moderate
```

Check .NET dependencies for known vulnerabilities:

```powershell
cd backend
dotnet list FlowOps.slnx package --vulnerable --include-transitive
```

Production deployments must provide `ConnectionStrings__DefaultConnection`, `JwtSettings__SecretKey`, `JwtSettings__Issuer`, `JwtSettings__Audience`, and `Cors:AllowedOrigins` through the deployment environment or a secret manager. Cookie-backed session endpoints require the browser Origin to exactly match a configured CORS origin.
