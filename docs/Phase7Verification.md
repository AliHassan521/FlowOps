# Phase 7 — Incident Management verification

Verified locally on 2026-11-02 (Asia/Karachi), from baseline `22afdd4`, on `codex/phase7-incidents`.
No production deployment was performed. All 12 migrations are applied to local FlowOpsDb, FlowOpsPhase3Validation and FlowOpsSecurityValidation; the EF snapshot matches the model.

## Delivered behavior

Project incident register/detail screens, severity and service tracking, immutable incident numbers,
lead-controlled ownership, the Open → Investigating → Mitigated → Resolved → Closed lifecycle,
response notes, append-only timeline, resolution/root cause, version conflicts, fixed incident SLAs,
shared worker/SignalR notifications, project metrics and lazy overview cards. Domain rules and DTOs
remain independent of HTTP and EF. Phase 8 release tracking/full dashboard remains out of scope.

## Automated and functional evidence

- **151 backend tests pass in Release**, including all prior authentication, tenant, workflow,
  notifications and work-item SLA regressions. New incident tests cover the entire state graph,
  required ownership, immutable resolution/end time, exact SLA completion boundaries, all five tenant
  roles, assignment bypass attempts, invalid/malformed requests, pagination and enum bounds, foreign
  project/tenant IDs, archived projects, revoked membership, invalid/viewer owners, overposted fields,
  comments/timeline, notifications, metrics, stale browser versions and transactional stale writes.
- Incident and work-item SLAs are processed together in bounded batches. Tests cover warning/breach
  idempotency, policy archival, no clock reset, early/late completion without worker ticks, and retained
  completion after later ticks. Existing worker cancellation, scope, retry and concurrency tests pass.
- An assembly dependency test checks that Domain, Application and Contracts do not reference API,
  Infrastructure, EF Core or ASP.NET.
- **74 Angular tests pass**, including prior modules and new incident HTTP scoping/version contracts,
  forms, viewer restrictions, resolution validation, owner action restrictions, pagination, tenant-change
  cancellation, obsolete details/metrics, HTML text escaping, conflict draft preservation, live-update
  hints, resource-specific notification navigation and persisted dropdown selection.
- The first unrestricted final Angular run suffered a worker exit under host load. Test parallelism
  is now bounded to two jsdom workers using the supported Angular `runnerConfig`; the complete suite
  passes with assertions intact. No timeout or assertion was weakened.
- Backend Release build: **zero warnings/errors**. Angular production build: **458.01 kB initial**,
  within the existing 500 kB warning budget. Incident screens remain a lazy bundle.
- Real SQL Server/HTTP/SignalR smoke (`backend/tests/phase7-smoke.cjs`) passes: concurrent same-version
  writes yield one 200/one 409 and concurrent SLA attachments one 201/one 409, outsider reads are denied, the actual hosted worker records a real
  two-minute incident warning/breach once, live recipient hints arrive, incident/work-item inbox
  references coexist correctly, resolution/closure and metrics persist, and the existing task lifecycle
  and SLA still work. A real 64 KiB incident request-body rejection is also checked locally.
- Browser functional verification used only synthetic data in `FlowOpsSecurityValidation`: login,
  overview incident counts, workspace/project navigation, saved role and owner display, investigation,
  mitigation, required resolution/root cause, closure, retained history, read-only terminal details,
  and an appended response note. Screenshot evidence is retained outside Git in the local review artifacts.

## Problems discovered during verification

Browser testing caught native select controls initially displaying their first option rather than the
saved role/owner/assignee when options were created dynamically. Explicit option selection fixes
organization role controls, existing work-item assignment, and incident ownership. DOM-level regression
tests now verify the saved values. Server authorization was already correct and was not relaxed. Final review also aligned concurrent incident SLA attachment with the existing work-item unique-conflict handling; the SQL smoke actually reaches the unique incident-SLA index, verifies one attachment succeeds and the other returns 409, and confirms only one warning/breach cycle is retained.

## Database and reproducibility

Migration `20261102172818_AddIncidentManagement` adds Incidents, IncidentComments, IncidentSlas,
indexes/checks/FKs and an incident notification reference. It preserves existing work-item notification
rows and requires exactly one resource reference. A real SQL Server downgrade attempt is blocked before schema changes when incidents exist, preserving
operational history. Apply all **12 migrations** before starting this API version; use matching API/UI
versions because new incident notifications have a nullable workItemId and populated incidentId.

```powershell
dotnet test backend/FlowOps.slnx --configuration Release
dotnet build backend/FlowOps.slnx --configuration Release
cd frontend
npm test -- --watch=false
npm run build
```

For the real worker flow, run the API from the repository root against a dedicated local database:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ConnectionStrings__DefaultConnection = 'Server=.\SQLEXPRESS;Database=FlowOpsSecurityValidation;Trusted_Connection=True;TrustServerCertificate=True;'
$env:SlaWorker__Enabled = 'true'
$env:SlaWorker__IntervalSeconds = '1'
$env:SlaWorker__BatchSize = '10'
dotnet run --no-launch-profile --project backend/src/FlowOps.Api --urls http://localhost:5144
```

In a second terminal run `node backend/tests/phase7-smoke.cjs`. It creates synthetic users and an
organization, waits for the real two-minute measurement, then soft-deactivates that organization.
Synthetic accounts and history remain. Never point this workflow at a production database.

`docs/Rules.md` now makes functional/failure/edge/security and affected-flow regression testing a
standing commit gate for every feature/module/phase, with full suites and release builds at milestones.

## Remaining limits

No reopening, SLA reset, business-hours calendar, releases, full engineering dashboard, outbox/backplane,
production load/recovery certification or CI/CD deployment was added. Incident numbers intentionally
use full GUIDs rather than sequential numbers. Existing work-item SLA metrics retain their original
scope; incident metrics are separate. Current membership/account checks apply to API reads/writes;
notification delivery remains a best-effort hint backed by the persisted inbox and polling.
The existing 16 moderate development-tool npm audit entries remain; runtime package audits are clean.
Learning checkpoints remain open and are not signed off by automated tests.
