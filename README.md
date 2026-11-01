# FlowOps

FlowOps is a modular engineering-operations platform for work tracking, incidents, SLAs, releases, notifications, and audit history.

## Current milestone

Phases 2 through 5 have working local implementations; learning sign-off is pending:

- BCrypt password hashing
- JWT access tokens held by the Angular client in memory
- Rotating refresh tokens stored only as hashes in SQL Server
- Refresh-token family revocation when a rotated token is replayed
- HttpOnly refresh-token cookie
- Exact configured Origin checks on cookie-backed refresh and logout
- Role-based authorization policies
- Authentication rate limiting
- Angular registration and login, route guard, interceptor, refresh, and logout flow
- API integration tests for the authentication pipeline
- Organization creation, listing, update, and soft deactivation
- Tenant-specific organization memberships and roles
- Member role management, revocation, and last-active-admin protection
- Project creation, listing, details, updates, and archival
- Project keys unique within an organization
- Tenant authorization enforced from active database membership
- Append-only audit events for organization, membership, and project changes
- Angular organization, member, and project workspace UI
- Tenant-scoped work item management for projects, including task/bug creation, assignment, priorities, workflow transitions, comments, filters, semantic sorting, pagination, and activity history
- Responsive Angular work-item list and detail experience with role-aware actions
- Persisted assignment, status-change, and comment notifications for active recipients
- A paginated notification inbox with unread counts, read actions, and links to exact work items
- Authenticated SignalR updates, reconnect recovery, and a periodic persisted-inbox fallback
- Optimistic concurrency protection for work-item writes and UTC timestamp read conversion
- API-scoped bearer credentials, safe refresh retries, stale-response protection, and later-page access in workspace/detail views

The automated API tests use SQLite in memory, including a real SignalR test-server connection.
All nine SQL Server migrations were applied to the configured `FlowOpsDb` database and
the isolated `FlowOpsPhase3Validation` database. Coverage currently has 41 backend tests and 37 Angular tests.
The Angular production build passes, and the production npm audit reports zero
vulnerabilities. The full npm audit still reports 16 moderate development-tool dependency findings.
A live SQL Server and SignalR smoke flow verified notification delivery, offline recovery,
read state, ownership, UTC timestamps, pagination, and membership revocation. The local
Angular app and API can be run using the steps below.

This is not yet a complete or production-ready product. Incidents, releases, SLAs,
CI, containerized local development,
operational hardening, and deployment practice remain future phases. No deployment
has been performed. Phase 2 authentication, Phase 3 multi-tenant, and Phase 4
workflow and Phase 5 real-time learning checkpoints remain open. The .NET vulnerability audit uses NuGet's vulnerability-
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

## Notifications and verification

The inbox is available from the bell in the workspace header. Assignment notifies the new assignee;
status changes and comments notify the reporter and assignee, excluding the actor and inactive recipients.
Notification reads always enforce current organization membership and active organization/project access.
The SignalR endpoint is `/api/hubs/notifications`. It sends only a recipient-specific invalidation hint;
the client retrieves the authorized persisted state, refreshes after reconnecting, and polls every 30 seconds.
A failed live hint does not discard a committed notification. Delivery is best effort; an outbox and
multi-instance SignalR backplane are not implemented.

For an optional real SQL Server smoke test, run a separate API against the migrated validation database:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=.\SQLEXPRESS;Database=FlowOpsPhase3Validation;Trusted_Connection=True;TrustServerCertificate=True;'
dotnet run --no-launch-profile --project backend/src/FlowOps.Api/FlowOps.Api.csproj --urls http://localhost:5155
```

Then, from the repository root in a second terminal:

```powershell
node backend/tests/phase5-smoke.cjs
```

The smoke test creates synthetic accounts and a workspace in the selected local database, then
soft-deactivates that workspace. Synthetic accounts and audit history remain. Do not target production.
See [Phase 5 verification](docs/Phase5Verification.md) for the completed checks and remaining limits.
