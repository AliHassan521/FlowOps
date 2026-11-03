# Phase 8 verification — 2026-11-03

Release tracking and the engineering dashboard implement the remaining core PRD product areas. Learning sign-off remains open.

## Delivered behavior

- Project environments, archived without erasing deployment history; normalized names unique within a project.
- Releases record one deployment attempt through Draft → Approved → Deploying → Successful/Failed. Retries use new drafts. This application records deployments; it does not execute them.
- Active non-viewers create drafts. The creator or a tenant lead edits notes and related resources while Draft; Admin/TeamLead approve and record deployment outcomes. Approved details and links are immutable. Outcomes and lifecycle timestamps remain durable.
- GUID versions protect notes, links and transitions. Audit and notification writes commit atomically with lifecycle changes; existing notification authorization also covers releases.
- SQL aggregates cover active project work, critical/unassigned items, active incidents, combined SLA risks/breaches/met, windowed resolution/release counts, bounded team workload and ten recent releases. Every query requires current organization access; project filtering rejects foreign/archived IDs.
- The dashboard cancels obsolete requests when scope changes and coalesces notification hints. Release editing preserves unsaved notes on conflicts and cancels obsolete detail/candidate/timeline requests.

## Verification

- 177 Release backend tests and 86 Angular tests pass, including all existing regression suites. Angular production build: 461.41 kB initial bundle, within its existing budget. Backend Release compilation succeeds.
- Thirteenth migration applied successfully to `FlowOpsSecurityValidation`; existing `FlowOpsDb` and `FlowOpsPhase3Validation` were not modified in this milestone.
- `node backend/tests/phase8-smoke.cjs` passed against that isolated SQL Server API: duplicate-environment race, overlapping link insertion, stale/concurrent notes, concurrent approval, atomic notification/timeline results, role/tenant boundaries, immutable approved links, bounded lists, dashboard SQL translation/counts, and prior work-item/incident lifecycles.
- Maximum-length Unicode notes (8,000), deployment outcome (4,000), and incident resolution plus root cause (4,000 each) persisted successfully on SQL Server.
- Real browser verification: sign-in, workspace navigation, project/window dashboard filtering, recent-release deep link, create draft, notes save, approval, deployment start, failed outcome, terminal read-only history, and work/incident link save. No browser console errors observed. Temporary test tenant archived and credential artifact removed; temporary servers stopped.
- Runtime npm audit and direct/transitive NuGet audit: no known vulnerabilities reported. The previously documented moderate development-tool findings remain separate; no forced Angular downgrade was applied.

## Issues found and fixed

1. Dashboard filtering on a constructed record could not translate to SQL. Filter/sort now use an anonymous SQL projection before constructing response records. Both SQLite integration tests and the real SQL Server flow pass.
2. Release candidate loading omitted required work-item sort fields. Strict Angular compilation caught this before publication.
3. The prior `nvarchar(2000)` audit details column could reject valid long incident resolution/root-cause JSON. It is widened to `nvarchar(max)` with a model maximum of 65,536. The SQL Server boundary test verifies the fix.
4. Maximum application/version/environment names could exceed the notification message column. Shared notification formatting bounds messages to 300 UTF-16 code units without splitting a surrogate pair.
5. Work throughput must use a recorded resolution timestamp rather than mutable `UpdatedAtUtc`. The new nullable value survives closure/metadata updates, clears on reopen, and backfills only from genuine resolution audit events. Unknown historical dates remain unknown.

## Operational limits

This evidence does not certify absence of every defect or production readiness. Dashboard queries are bounded but have not been load-tested at production scale; multi-query counts can reflect concurrent updates rather than one historical snapshot. Counts of resolved items describe items currently resolved/closed, not every historical resolution event. SLA breach/met counters include persisted historical attachments, consistent with existing SLA behavior. SignalR remains a single-instance hint transport with persisted inbox recovery, without a distributed backplane/outbox. There is no automated deployment executor or hosting destination configured.

Migration rollback refuses to discard release/environment records or truncate audit text; use a reviewed backup/recovery plan rather than destructive downgrades after data has been written.
