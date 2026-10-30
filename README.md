# FlowOps

FlowOps is a modular engineering-operations platform for work tracking, incidents, SLAs, releases, notifications, and audit history.

## Current milestone

Phases 2 and 3 have working implementations; learning sign-off is pending:

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
- Organization creation, listing, update, and soft deactivation
- Tenant-specific organization memberships and roles
- Member role management, revocation, and last-active-admin protection
- Project creation, listing, details, updates, and archival
- Project keys unique within an organization
- Tenant authorization enforced from active database membership
- Audit events for organization, membership, and project changes
- Angular organization, member, and project workspace UI

The automated API tests use SQLite in memory. SQL Server migrations were applied to
the configured SQL Server Express database and to an isolated Phase 3 validation
database. Integration coverage currently has 27 backend tests and 17 Angular tests.
The Angular production build passes, and the production npm audit reports zero
vulnerabilities. A live SQL Server smoke flow verified registration, organization
creation, project creation, listing, and authenticated reads. The local Angular app
and API can be run using the steps below.

This is not yet a complete or production-ready product. Work items and workflow,
incidents, releases, SLAs, notifications, CI, containerized local development,
operational hardening, and deployment practice remain future phases. No deployment
has been performed. The Phase 2 authentication and Phase 3 multi-tenant learning
checkpoints remain open. The .NET vulnerability audit uses NuGet's vulnerability-
only feed and completed with no vulnerable direct or transitive packages reported.

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
