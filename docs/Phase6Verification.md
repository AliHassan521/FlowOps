# Phase 6 verification — 2 November 2026

Phase 6 adds SLA policies, fixed work-item deadlines, and background warning/breach processing.
This is a local implementation milestone. Developer learning checkpoints and production release gates remain open.

## Behavior

Admins and TeamLeads create organization policies and attach them to open work items. Each item has
one immutable measurement, started at attachment, with captured policy settings. Clocks use continuous
UTC elapsed time. A zero-minute warning disables warnings; other warning lead times must be shorter
than the duration. Policy archival preserves existing deadlines. Resolution completes the measurement,
preserving a breach when completion is late. Reopening does not reset that historical measurement.

The worker queries bounded batches on a configurable interval and creates a new DI scope per item.
Concurrency tokens on the SLA and parent work item protect worker/worker and worker/resolution races.
Each successful write commits the marker, system audit event, and recipient notifications together.
Live SignalR hints are best effort after commit; missed hints are recovered from persisted state.

## Checks

- 65 backend tests pass, including SLA calculation and boundary validation, skipped obsolete warnings,
  repeat processing across fresh scopes, overlapping processors, resolution racing a warning,
  early/late completion, tenant/role controls, policy archival, metrics, cancellation, batch limits,
  failed-record isolation, and existing authentication/workflow/notification behavior.
- 47 Angular tests pass, including policy validation and archive confirmation, tenant-specific HTTP
  routes, work-item SLA display, viewer restrictions, stale-response cancellation, conflict messages,
  deferred SLA rendering, and logout during deferred real-time client loading.
- Backend Release build passes with zero warnings/errors using isolated output.
- Angular production build passes within the existing size budget: 452.39 kB initial raw size,
  approximately 112.43 kB estimated transfer. SLA views and the SignalR protocol client load on demand.
- EF reports no pending model changes. All ten migrations are applied to local `FlowOpsDb` and
  `FlowOpsPhase3Validation`; the new migration adds SLA tables and permits a null system audit actor.
- `backend/tests/phase6-smoke.cjs` passes against a temporary local API with real SQL Server and a
  one-second hosted-worker interval. It observes a real two-minute SLA, verifies its warning and
  breach plus SignalR hints, checks no duplication over later ticks, and verifies early/late resolution,
  unchanged deadlines after policy archival, system audit history, and tenant SLA metrics.
- Synthetic smoke-test workspaces are soft-deactivated afterward; synthetic accounts and audit
  records remain. No production database is used, and no test-clock or worker-trigger HTTP bypass is exposed.
- Production npm and direct/transitive NuGet advisory scans report no known vulnerable dependencies.
  The previously documented 16 moderate development-tool affected-package entries remain unresolved.

## Configuration and compatibility

`SlaWorker` defaults to enabled, 30 seconds per tick and 100 candidate records per batch. Interval
must be 1–3600 seconds and batch size 1–500. Invalid configuration fails startup. Tests disable the
hosted loop and drive isolated processors with an injected clock; the live smoke uses the real clock.
Apply migrations before starting the worker.

`WorkItemHistoryResponse.actorUserId` is now nullable. System entries display `actorName: "System"`.
API consumers that assumed every audit entry has a user identifier must handle the null case.
The migration's downgrade refuses to rewrite system actors or delete immutable audit records; use
a reviewed forward repair migration after such records exist.

## Intentional limits

- This phase implements continuous-time work-item SLAs. Business calendars, pauses, extensions,
  multiple/restarted cycles and incident SLAs are not implemented.
- The query and each record are bounded, sequential work. A failed record is logged and retried
  on later ticks; there is no durable retry/dead-letter queue or dedicated worker health dashboard.
- Warning/breach records and notifications are protected from duplicate committed processing.
  Network delivery is not exactly-once; SignalR hints are best effort without an outbox/backplane.
- Production deployment, CI, ingress/secret validation, load/soak testing and backup/restore drills
  remain later phases. Passing these local checks is not a claim of production readiness.
- Phase 2–6 understanding/build/debug/defense checks remain open. Phase 7 (Incident Management)
  is the next implementation phase, on the user's direction.
