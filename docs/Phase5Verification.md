# Phase 5 verification — 2 November 2026

Phase 5 implements persisted work-item notifications and live inbox updates. This is a verified local
milestone, not a production deployment or a sign-off of the developer's learning checkpoints.

## Completed checks

- 41 backend tests pass, including notification ownership, current membership revocation, unread/read
  behavior, explicit pagination, recipient deduplication, transport-failure durability, stale workflow
  rollback, required assignment, and UTC read conversion.
- A real SignalR connection through the ASP.NET test server verifies recipient-only hints, empty
  payloads, persistence before delivery, and offline notification recovery. Hub origin/authentication
  tests verify that query-string tokens cannot authenticate normal API endpoints.
- 37 Angular tests pass, including initial inbox loads, hints, reconnect recovery, polling/retry,
  logout cleanup, read state, pagination, bell rendering, exact notification links, credential scoping,
  refresh retry errors, stale detail responses, and later-page comment/history access.
- Angular production build passes within the existing 500 kB initial warning budget (497.81 kB).
- Backend Release build passes with zero warnings and zero errors using isolated output.
- The EF model has no pending migration changes.
- All nine migrations are applied to local `FlowOpsDb` and `FlowOpsPhase3Validation`. The previously
  missing work-item migration in the default database is now applied.
- `backend/tests/phase5-smoke.cjs` passed against a local API on port 5155 using the isolated SQL Server
  validation database. It verified persisted notifications, live SignalR hints, UTC timestamp round trips,
  ownership, read/unread state, offline recovery, pagination, and membership revocation. The synthetic
  workspace was soft-deactivated; synthetic accounts and history remain.
- The browser inbox rendered against the default local database with a persisted assignment and
  live connection status using synthetic local users.
- The production npm audit reports zero known vulnerabilities after adding Microsoft's SignalR client.
  The most recent NuGet audit reports no known vulnerable direct/transitive packages.

## Related audit fixes

The Phase 4 audit identified workflow races, UTC serialization errors, invalid unassignment,
unscoped bearer headers, session clearing after a resource error, stale UI responses, and first-page-only
collections. These are addressed by concurrency tokens, UTC conversion, assignment invariants,
API-scoped interception, separated refresh failure handling, cancellable/identity-checked UI requests,
and explicit paging/load-more controls. Server concurrency protection does not yet provide browser
ETag/expected-version editing contracts.

## Remaining limits

- The full npm audit still reports 16 moderate development-tool affected-package entries. Production
  dependencies are clean according to the advisory feed. Do not apply npm's suggested incompatible
  Angular CLI major downgrade without compatibility review.
- Live delivery is best effort. There is no durable outbox or multi-instance backplane. Offline/missed
  events are recovered from persisted notifications on reconnect or the 30-second polling fallback.
- No deployment, CI pipeline, production load/soak run, backup/restore drill, or production ingress/secret
  validation has been completed. These remain later release gates.
- Component rendering on a heavily loaded host intermittently exceeded Vitest's default five-second
  timeout; the final full run passed without increasing timeouts or weakening assertions.
- Phase 2–5 learning/understanding checks remain open. Phase 6 is the next implementation phase.

## Runtime model

A business operation chooses eligible recipients, appends notifications with its work-item/audit
changes, and commits. Only then does the dispatcher send a recipient-specific invalidation hint.
The browser reloads the inbox through the normal authenticated API, which checks current tenant
membership. If the connection fails, the database record remains the authoritative notification.
