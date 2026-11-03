# FlowOps

FlowOps is a modular engineering-operations platform for work tracking, incidents, SLAs, releases, notifications, and audit history.

## Current milestone

Phases 2 through 8 have working local implementations; learning sign-off is pending:

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
- Organization SLA policies, fixed work-item deadlines, warning thresholds, and SLA metrics
- A configurable scoped background worker with durable, concurrency-protected warning/breach processing
- SLA notifications, system audit history, and completion that preserves late breaches
- Angular SLA policy management and work-item deadline/status views, loaded on demand
- Incident lifecycle, ownership, response notes/timeline, resolution/root cause and fixed SLA monitoring
- Incident summary cards, resource-specific inbox links and versioned incident edits

- Release environments, versioned draft/approval/deployment lifecycle, immutable approved links, outcomes and release notifications
- Organization/project engineering dashboard with SQL aggregates, resolution windows and bounded workload/recent releases

The automated API tests use SQLite in memory, including a real SignalR test-server connection.
Thirteen migrations are validated on the isolated `FlowOpsSecurityValidation` database; the other two local databases remain at twelve until upgraded. Coverage currently has 177 backend tests and 93 Angular tests. See [Phase 8 verification](docs/Phase8Verification.md) and [operations/setup](docs/Operations.md).
The Angular production build passes, and the production npm audit reports zero vulnerabilities. The full npm audit still reports 16 moderate development-tool dependency findings.
A live SQL Server and SignalR smoke flow verified notification delivery, offline recovery,
read state, ownership, UTC timestamps, pagination, and membership revocation. The local
Angular app and API can be run using the steps below.

CI and a loopback-only Docker development setup are implemented; their current verification status is recorded in [Operations verification](docs/OperationsVerification.md). The latest backend/frontend CI jobs pass, while an expanded proxy smoke failure and an earlier inconsistent backend run remain open in that report. Production hosting, operational capacity/restore validation, and deployment practice are deferred to the user’s later learning phase. No deployment
has been performed. Phase 2 authentication, Phase 3 multi-tenant, and Phase 4
workflow, Phase 5 real-time, Phase 6 background-processing, and Phase 7 incident and Phase 8 release/dashboard learning checkpoints remain open. The .NET vulnerability audit uses NuGet's vulnerability-
only feed and completed with no vulnerable direct or transitive packages reported.

## Local setup

Requirements: .NET 10 SDK, Node.js 24 LTS and npm 11.21.1, Angular CLI, SQL Server Express/local SQL Server.

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
   npm install --global npm@11.21.1
   npm ci
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

## SLA policies and monitoring

Open **SLA policies and metrics** from an organization workspace. Admins and TeamLeads can create
and archive policies. Open a work item and select a policy in its SLA panel to start a fixed measurement.
Clocks run continuously in UTC; the due-date field remains independent of the SLA deadline.
A warning lead time of 0 disables warnings. Each item supports one measurement, which cannot be reset.
Resolution completes it; late resolution retains the breach, and reopening does not restart its clock.
Archiving a policy prevents new attachments and leaves existing measurements intact.

The hosted worker starts with the API. Configuration in `SlaWorker` supports `Enabled` (default true),
`IntervalSeconds` (default 30; range 1–3600), and `BatchSize` (default 100; range 1–500).
Environment overrides use names such as `SlaWorker__IntervalSeconds`. Apply migrations before starting it.
Measurements, audit events, and notifications are committed together, and live SignalR hints follow the commit.
Work-item history now permits a null `actorUserId` for system-generated entries and displays the actor as **System**.

To verify the real hosted worker, use the isolated API setup above with port **5156** and add:

```powershell
$env:SlaWorker__Enabled = 'true'
$env:SlaWorker__IntervalSeconds = '1'
$env:SlaWorker__BatchSize = '10'
```

Then run `node backend/tests/phase6-smoke.cjs` in a second terminal. It deliberately waits for a real
two-minute SLA, creates only synthetic local data, and soft-deactivates its test workspace afterward.
See [Phase 6 verification](docs/Phase6Verification.md) for lifecycle, concurrency, and operational limits.

## Adversarial security review

See [the verification report](docs/AdversarialVerification.md) for reproduced problems, fixes,
SQL Server race and migration tests, HTTP security checks, commands and remaining production gates.
Refresh/logout now serialize per login family; apply all twelve migrations before running this version.
Logout clears the local session immediately and revokes the refresh family. Active bearer tokens
expire on their normal short lifetime; disabling an account rejects new authenticated requests.
SLA failures receive a bounded retry cooldown so later records can proceed.

## Incident management

From a project, open **Incidents** to report an outage, assign an owner, record the response and
resolve/close it. Severity and affected service are explicit; resolution is required and root cause
is recorded when known. The incident timeline and resolution remain preserved after closure.
Admins/TeamLeads manage ownership, fixed SLA attachment and closure. Current owners can progress
investigation, mitigation and resolution. Viewers remain read-only.

Incident SLAs share the existing hosted worker and notification inbox; the overview and project
register show scoped incident aggregates. Version conflicts request a refresh, and live hints never
overwrite an in-progress draft. Apply all **12 migrations** and run the matching API and Angular version.
See [Phase 7 verification](docs/Phase7Verification.md) for functional/regression coverage and commands.
Future changes must pass the standing testing gate in [development rules](docs/Rules.md).
