# Adversarial verification — phases 0–6

Date: 2026-11-02. Baseline: `650b8f0` (Phase 6). Scope: the implemented authentication,
organization/membership, project, work-item/workflow/comment/history, notification/SignalR,
and SLA features. This is a local engineering review with automated abuse cases, not a
certification that every possible bug or vulnerability has been eliminated.

## Findings and changes

| Finding | Impact | Fix and evidence |
| --- | --- | --- |
| Creation accepted an assignee from Engineer/QA although reassignment was lead-only | Unauthorized assignment | Require Admin/TeamLead for a non-null creation assignee; hide the picker for other roles and discard injected form values. Both bypass cases failed before the fix. |
| Logout with a rotated cookie left its descendant valid; replay could race insertion | Session revocation bypass | Add a stable `RefreshSessions` row. Refresh, replay and logout lock that row before changing tokens. Logout revokes the whole login family; replay revocation commits before returning 401. SQL Server races verify the descendant is unusable and independent logins survive. |
| Late refresh/login/logout responses could restore stale identity; a prior request could retry under a newer user | Incorrect session and request identity | Serialize cookie-changing requests and gate local responses/retries by an identity revision. Keep an already-started cookie request alive after unsubscription. Regression tests cover refresh/logout, refresh/new-login, logout/new-login and old-resource/new-user ordering. |
| Every refresh failure cleared the session | Avoidable logout during throttling/outage | Preserve the session for 429/503; clear it on authentication rejection. Test parallel 401 requests share one rotation. |
| Unknown-account dummy BCrypt cost was 10 while real hashes use 12 | Account timing discrepancy | Match the dummy hash cost to 12. Structural review and bad/unknown credential tests; no claim of a measured remote timing exploit. |
| Some routes accepted an inactive account's still-valid JWT; notifications filtered to an empty inbox | Inconsistent account revocation | Validate the active user at bearer authentication, including profile, tenant routes and hubs. Missing/inactive identities fail closed. Tenant roles remain checked against current membership by services. |
| A failing first SLA record repeatedly occupied a batch of size one | Later deadlines could starve | Add a bounded in-memory retry cooldown and exclude cooling records at selection. Tests verify later work progresses and failed work becomes eligible again. |
| The error handler converted Kestrel's oversized-body exception into 500 | Incorrect error status and noisy server errors | Preserve HTTP client-error status codes, sanitize parser details. Reproduced on real Kestrel with a login body over 16 KiB, then rerun after correction. |

## Verification matrix

- Authentication: hashed credentials/tokens, registration bounds and safe role, duplicate email,
  bad/unknown/inactive accounts, expired/tampered/wrong-issuer/wrong-audience/future/unsigned JWTs,
  nonexistent JWT subjects, rotation/replay/logout, secure HttpOnly cookie attributes, trusted-origin
  enforcement including lookalikes, CORS, rate limits, actual Kestrel request size enforcement.
- Authorization: all five tenant roles for reads, creation, assignment and comments; outsider resource
  reads, nested comment/history/SLA routes, foreign organization/project substitution, archived resources,
  current membership checks, last-admin protection, viewer restrictions and workflow transition rules.
- Input/data integrity: null/missing/malformed JSON, invalid enums/types/GUIDs/dates, negative and
  overflowing pages, oversized pages, invalid sorting, exact Unicode title/description limits,
  SQL/HTML payloads, overposted reporter/status/tenant/role fields, parameterized search, UI text escaping.
- Transactions/concurrency: stale work-item updates roll back business/audit/notification writes;
  duplicate event prevention; independent DbContexts; real SQL Server synchronized refresh races.
- Notifications: current tenant visibility, read idempotency, recipient isolation, no actor notifications,
  offline persistence, reconnect/poll behavior, failed live hint after commit, authenticated hubs and origins.
- SLA: warning/deadline boundaries, UTC, disabled warning, missed warning, early/late resolution,
  reopen semantics, archived policies/projects, duplicate ticks/overlapping processors, cancellation,
  poisoned records and cooldown retry, summary scope, real two-minute hosted-worker/SignalR flow.
- Frontend: auth ordering and identity isolation, session failure paths, tenant/list/detail responses,
  form validation, role actions, deferred SLA panels, notification UI and service behavior.

Final results: **119 backend tests**, **58 Angular tests**; Release backend build (zero warnings/errors)
and Angular production build pass. Initial production frontend: **453.69 kB**.
The SQL smoke performs **10 synchronized refresh/replay races and 10 refresh/logout races**.
The real hosted worker smoke and HTTP security smoke pass. NuGet including transitive packages and
npm runtime dependency audits report no known vulnerabilities in their current advisory sources.

## Migration and reproduction

`20261102061201_SerializeRefreshSessions` backfills existing token families before adding the foreign key.
It refuses inconsistent legacy families shared by multiple users. A fresh local security database was
first migrated to Phase 6, seeded with a live legacy token, upgraded, then successfully refreshed/revoked.
All eleven migrations are applied locally to `FlowOpsDb`, `FlowOpsPhase3Validation` and the dedicated
`FlowOpsSecurityValidation` database. EF reports no pending model changes. No production database is used.

```powershell
dotnet test backend/FlowOps.slnx
dotnet build backend/FlowOps.slnx --configuration Release
dotnet run --project backend/tests/FlowOps.SecuritySmoke
cd frontend
npm test -- --watch=false
npm run build
npm audit --omit=dev
dotnet list ../backend/FlowOps.slnx package --vulnerable --include-transitive
```

The SQL console is fixed to `.\SQLEXPRESS` / `FlowOpsSecurityValidation`; it never accepts a production
connection or deletes a database. The legacy-upgrade fixture runs on its first creation. Synthetic users
and evidence remain; its concurrency fixture account is deactivated at the end. Secrets are generated
in memory and are never printed.

Use the existing README's local API setup on port 5156, pointed at the migrated security validation
database, to run `node backend/tests/phase6-smoke.cjs`. Afterwards run
`node backend/tests/security-http-smoke.cjs`; it intentionally consumes the authentication rate limit.
Stop this isolated API after verification. API integration tests freeze JWT validation time separately
from the SLA test clock, retaining signature, issuer and audience validation; desktop suspension must
not make unrelated tests fail because their 15-minute JWT elapsed.

## Remaining risks and production gates

- Full npm audit still reports **16 moderate development-tool dependency entries**, originating in
  `glob` and `minimatch` under Angular CLI. Runtime audit is clean. Upstream fixes require major versions
  (`glob >=14.0.2`, `minimatch >=11.0.1`); npm proposes a disruptive CLI downgrade. No forced upgrade,
  override or downgrade was applied without compatibility evidence. Keep build inputs trusted and track
  compatible upstream updates. Advisory sources:
  [glob](https://github.com/isaacs/node-glob/security/advisories/GHSA-3g3m-2fjr-6pw4),
  [minimatch](https://github.com/isaacs/minimatch/security/advisories/GHSA-g3hv-75wg-m249).
- No production deployment, independent penetration test, sustained load test, backup restoration,
  disaster recovery, proxy/TLS configuration verification or CI/CD release gate was performed here.
  Angular checks use service/component tests in jsdom; this audit does not claim a cross-browser suite.
- Rate limiting is per API process/IP. Distributed throttling and trusted reverse-proxy configuration
  need deployment-specific design. Existing access tokens remain valid after logout until their short
  expiry; refresh descendants are revoked. Inactive accounts are checked on new authenticated requests;
  an already-open hub connection expires with its JWT and carries no resource payloads.
- SignalR delivery remains best effort without an outbox/backplane; the persisted inbox and polling recover
  missed hints. Worker cooldown state is local, resets on restart and tracks at most 1000 failures; a large
  poison backlog still needs operational intervention and a durable retry/dead-letter design.
- This phase has no automatic cleanup for expired refresh/session rows. Retention, monitoring and
  capacity policies must be established before a long-running production deployment.

Do not describe the application as vulnerability-free or fully production-certified on the strength
of this review. The learning checkpoints and future phase/deployment acceptance checks remain open.
