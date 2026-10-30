# FlowOps Development Phases

## 1. Purpose

This file is the primary execution plan.

Always work on the **current phase only** unless a critical dependency requires otherwise.

Do not jump ahead because another technology looks interesting.

## Current execution status

- **Current phase:** Phase 3 — Organizations, Projects & Membership
- **Current task:** Complete the Phase 3 architecture and learning review before starting Phase 4.
- **Verified so far:** Phase 2 implementation and security checks; Phase 3 SQL Server Express migrations; 27 backend integration tests; 17 Angular tests; clean Angular production build; production npm audit (0 vulnerabilities); .NET vulnerability audit (no vulnerable direct or transitive packages reported); successful NuGet restore using the repo audit-source configuration; live local SQL Server API smoke flow covering registration, organization creation, project creation/listing, and organization/project updates. The browser window review was interrupted because the selected Chrome window was minimized during user activity, so no browser visual check is claimed.
- **Learning status:** The Phase 2 authentication understanding check remains pending. Phase 3 technical checks pass; its multi-tenant understanding check is pending.
- **Local run:** API at `http://localhost:5144` and Angular at `http://localhost:4200`, using the isolated `FlowOpsPhase3Validation` SQL Server Express database. No deployment has been performed.
- **Next phase:** Phase 4 — Work Items & Workflow Engine, after the Phase 3 learning checkpoint.

---

# 2. Phase System

Each phase has:

- Goal
- Tasks
- Acceptance criteria
- Understanding check
- Build check
- Debug check
- Interview/defense check

A phase is only considered complete when all required checks pass.

---

# Phase 0 — Project Setup

## Goal

Create a clean development environment and repository.

## Tasks

- [ ] Create GitHub repository
- [ ] Create root project structure
- [ ] Create documentation folder
- [ ] Create .NET solution
- [ ] Create Angular application
- [ ] Verify SQL Server connectivity
- [ ] Configure Git
- [ ] Add initial `.gitignore`
- [ ] Create initial commit
- [ ] Verify backend starts
- [ ] Verify Angular starts

## Acceptance Criteria

- Backend runs locally.
- Angular runs locally.
- Git repository works.
- Project structure is understandable.
- No secrets are committed.

## Understanding Check

Without AI, explain:

- What the backend does.
- What the frontend does.
- How Angular communicates with the API.
- Where SQL Server fits.

## Build Check

Can you recreate a minimal API endpoint without copying the existing endpoint?

## Debug Check

Can you identify why the API would fail to start?

## Interview Check

Explain the high-level architecture in under 2 minutes.

---

# Phase 1 — Foundation Architecture

## Goal

Establish the backend structure and database foundation without implementing the whole product.

## Tasks

- [x] Define solution/project boundaries
- [x] Configure dependency injection
- [x] Configure application settings
- [x] Configure EF Core
- [x] Configure database connection
- [x] Create initial database
- [ ] Create core organization/user-related tables (Phase 2)
- [x] Configure migrations
- [x] Configure Swagger/OpenAPI
- [x] Add global error handling
- [x] Add basic health endpoint

## Acceptance Criteria

- API starts cleanly.
- Database can be created/updated.
- Swagger works.
- Health endpoint works.
- Architecture follows Architecture.md.

## Understanding Check

Explain:

- Dependency injection
- Configuration
- EF Core DbContext
- Middleware
- Why controllers should remain thin

## Build Check

Create a simple endpoint from scratch.

## Debug Check

Fix a broken DB connection without AI generating the solution for you.

## Interview Check

Explain why the project is a modular monolith.

---

# Phase 2 — Authentication & Authorization

## Goal

Create a secure authentication foundation.

## Tasks

- [x] Users table
- [x] Roles table
- [x] User-role relationship
- [x] Password hashing
- [x] Login endpoint
- [x] JWT generation
- [x] Refresh-token strategy
- [x] Authentication middleware/configuration
- [x] Authorization policies
- [x] Angular login page
- [x] Angular auth service
- [x] HTTP interceptor
- [x] Route guard
- [x] Logout/session handling
- [x] API integration coverage for authentication, authorization, CORS, rate limiting, refresh, and logout

## Acceptance Criteria

- Valid users can log in.
- Invalid credentials are rejected.
- Protected APIs reject unauthenticated access.
- Authorized roles can access permitted operations.
- Refresh tokens are stored as hashes, rotate once, and replay revokes only their login family.
- Cookie-backed refresh and logout reject absent or untrusted browser origins.
- Organization boundaries will be enforced when organizations and memberships are introduced in Phase 3.

## Understanding Check

You must be able to explain:

- Authentication vs authorization
- JWT
- Password hashing
- Access token vs refresh token
- Middleware
- Claims
- Role/policy authorization

## Build Check

Implement a small protected endpoint without AI assistance.

## Debug Check

Diagnose an unauthorized request.

## Interview Check

Explain the full login flow from browser to database and back.

---

# Phase 3 — Organizations, Projects & Membership

## Goal

Build the main organization/project structure.

## Tasks

- [x] Organization CRUD
- [x] Project creation
- [x] Project listing
- [x] Project details
- [x] Membership
- [x] Role-aware project access
- [x] Angular project pages
- [x] Validation
- [x] Audit important actions

## Acceptance Criteria

- Users only access authorized organizations/projects.
- Project keys are unique within an organization.
- UI reflects authorization.

## Understanding Check

Explain multi-tenant boundaries.

## Build Check

Create a new protected resource flow without copying another feature.

## Debug Check

Find and fix an authorization leak between organizations.

## Interview Check

Explain how you prevent cross-organization data access.

---

# Phase 4 — Work Items & Workflow Engine

## Goal

Move beyond CRUD by implementing real business rules.

## Tasks

- [ ] Work item model
- [ ] Task/Bug types
- [ ] Create/update/list
- [ ] Assignment
- [ ] Priorities
- [ ] Status model
- [ ] Workflow transition service
- [ ] Invalid transition handling
- [ ] Comments
- [ ] Filtering
- [ ] Sorting
- [ ] Pagination
- [ ] Audit history
- [ ] Angular work-item views

## Acceptance Criteria

- Work items can be managed.
- Invalid status transitions are rejected.
- Assignment rules work.
- Important changes are audited.
- Collection endpoints support pagination/filtering.

## Understanding Check

Explain why this is more than CRUD.

## Build Check

Implement a new workflow transition yourself.

## Debug Check

Diagnose why an invalid transition is being accepted/rejected.

## Interview Check

Explain the workflow/state-machine design.

---

# Phase 5 — Notifications & Real-Time Communication

## Goal

Add real-time user feedback.

## Tasks

- [ ] Notification entity
- [ ] Notification persistence
- [ ] Notification API
- [ ] SignalR hub
- [ ] Client connection handling
- [ ] Real-time event delivery
- [ ] Notification UI
- [ ] Mark-as-read behavior

## Acceptance Criteria

- Important events create notifications.
- Connected users receive appropriate real-time updates.
- Notifications remain persisted even if the user is disconnected.

## Understanding Check

Explain:

- WebSocket vs HTTP conceptually
- SignalR
- Persistent notification vs real-time push

## Build Check

Implement one new SignalR event.

## Debug Check

Diagnose a client that connects but receives no event.

## Interview Check

Explain why SQL Server remains the source of truth.

---

# Phase 6 — SLA & Background Processing

## Goal

Introduce asynchronous/background business processing.

## Tasks

- [ ] SLA policy model
- [ ] Deadline calculation
- [ ] Warning threshold
- [ ] Background worker
- [ ] Overdue detection
- [ ] Breach recording
- [ ] Notification creation
- [ ] SignalR update
- [ ] Idempotency protection
- [ ] Configurable processing interval

## Acceptance Criteria

- SLA deadlines are calculated correctly.
- Worker identifies approaching breaches.
- Worker records breaches exactly once.
- Relevant users are notified.

## Understanding Check

Explain:

- Why background processing is needed
- Hosted services
- Idempotency
- Cancellation
- Scoped service usage in workers

## Build Check

Create a small background worker yourself.

## Debug Check

Diagnose duplicate processing.

## Interview Check

Explain what happens when the worker restarts during processing.

---

# Phase 7 — Incident Management

## Goal

Build a serious operational workflow.

## Tasks

- [ ] Incident entity
- [ ] Severity
- [ ] Incident lifecycle
- [ ] Assignment
- [ ] Timeline
- [ ] Comments
- [ ] Root cause/resolution
- [ ] SLA integration
- [ ] Dashboard integration
- [ ] Audit events

## Acceptance Criteria

- Incidents follow valid lifecycle transitions.
- Timeline is preserved.
- Resolution is recorded.
- SLA behavior is visible.

## Understanding Check

Explain why incidents are modeled separately from ordinary work items.

## Build Check

Create a new incident-related business rule yourself.

## Debug Check

Diagnose an incorrect incident state transition.

## Interview Check

Walk through a critical incident from creation to resolution.

---

# Phase 8 — Release Tracking & Engineering Dashboard

## Goal

Create useful operational visibility.

## Tasks

- [ ] Release model
- [ ] Environment model
- [ ] Release lifecycle
- [ ] Link releases to work items
- [ ] Deployment status
- [ ] Dashboard aggregates
- [ ] Team workload metrics
- [ ] SLA widgets
- [ ] Incident widgets
- [ ] Recent releases

## Acceptance Criteria

- Dashboard metrics come from efficient queries.
- Release lifecycle works.
- Dashboard is useful without manually inspecting raw records.

## Understanding Check

Explain how dashboard metrics are calculated.

## Build Check

Write one aggregate query yourself.

## Debug Check

Use SQL output/query analysis to find a slow query.

## Interview Check

Explain indexing decisions for the dashboard.

---

# Phase 9 — Testing & Quality

## Goal

Make the core system trustworthy.

## Tasks

- [ ] Unit-test business rules
- [ ] Unit-test SLA calculations
- [ ] Unit-test workflow transitions
- [ ] Integration-test authentication
- [ ] Integration-test critical work-item flows
- [ ] Test failure paths
- [ ] Test authorization
- [ ] Add test execution to CI

## Acceptance Criteria

- Core business rules have automated tests.
- Important authorization flows are tested.
- CI can execute the test suite.

## Understanding Check

Explain unit vs integration testing and when to use each.

## Build Check

Write a test before asking AI to fix it.

## Debug Check

Diagnose a failing test.

## Interview Check

Explain how you decided what deserves tests.

---

# Phase 10 — Redis & Performance

## Goal

Introduce performance improvements based on actual needs.

## Tasks

- [ ] Add Redis locally
- [ ] Identify a valid cache candidate
- [ ] Implement caching
- [ ] Implement invalidation/expiration
- [ ] Measure before/after where practical
- [ ] Review database indexes
- [ ] Review pagination and query performance

## Acceptance Criteria

- Redis is used for a justified scenario.
- Cache behavior is predictable.
- Database remains the source of truth.

## Understanding Check

Explain caching, expiration, invalidation, and cache misses.

## Build Check

Implement one cache-backed read yourself.

## Debug Check

Diagnose stale cache behavior.

## Interview Check

Explain why the selected data is cached.

---

# Phase 11 — Docker & Local Production-Like Environment

## Goal

Make the project reproducible.

## Tasks

- [ ] Dockerfile for backend
- [ ] Dockerfile/build for frontend
- [ ] Docker Compose
- [ ] Environment configuration
- [ ] Local startup instructions
- [ ] Verify clean-machine style startup

## Acceptance Criteria

A documented command can start the main system consistently.

## Understanding Check

Explain:

- Image
- Container
- Volume
- Network
- Environment variable

## Build Check

Write a minimal Dockerfile yourself.

## Debug Check

Diagnose a container networking/configuration problem.

## Interview Check

Explain why Docker helps this project.

---

# Phase 12 — CI/CD & Deployment

## Goal

Automate quality checks and deployment.

## Tasks

- [ ] GitHub Actions workflow
- [ ] Restore dependencies
- [ ] Build backend
- [ ] Build frontend
- [ ] Run tests
- [ ] Build container
- [ ] Configure deployment
- [ ] Add deployment documentation
- [ ] Verify deployed application

## Acceptance Criteria

- Push triggers CI.
- Failed tests stop the pipeline.
- Successful code can be deployed through the configured workflow.

## Understanding Check

Explain CI vs CD.

## Build Check

Modify the CI workflow yourself.

## Debug Check

Diagnose a failed pipeline.

## Interview Check

Walk through the pipeline from git push to deployment.

---

# Phase 13 — Observability & Reliability

## Goal

Improve production readiness.

## Tasks

- [ ] Structured logging
- [ ] Correlation information
- [ ] Health checks
- [ ] Metrics
- [ ] Tracing where justified
- [ ] Review error handling
- [ ] Review retry behavior
- [ ] Review background-worker failure behavior

## Acceptance Criteria

A developer can investigate a failure using logs/health information without attaching a debugger to the running system.

---

# Phase 14 — Optional Differentiators

Only start after the core project is stable.

Possible features:

- [ ] GitHub integration
- [ ] GitHub webhooks
- [ ] AI incident analysis
- [ ] .NET Aspire exploration

These are optional.

They must never block the main project.

---

# 3. Understandability Tracking

Do NOT create a separate tracking file.

Track understanding inside this document and in GitHub issues/comments.

For each major feature, rate:

```text
Build:       0-3
Understand:  0-3
Debug:       0-3
Defend:      0-3
```

### Rating

`0 = cannot do/explain`

`1 = can follow with help`

`2 = can do/explain with minor help`

`3 = can do/explain independently`

Example:

```text
Authentication

Build:       3
Understand:  2
Debug:       1
Defend:      2
```

A feature should not be considered fully learned until:

```text
Build >= 2
Understand >= 2
Debug >= 2
Defend >= 2
```

For critical topics, target `3`.

---

# 4. No-AI Challenge Rule

After AI-assisted implementation of an important concept:

1. Stop looking at generated code.
2. Explain the concept in your own words.
3. Implement a smaller version independently.
4. Break/fail it intentionally if useful.
5. Debug it.
6. Compare your implementation with the project implementation.
7. Record your understanding score.

This prevents passive copying.

---

# 5. Phase Completion Rule

A phase is complete only when:

- Acceptance criteria pass.
- Tests for the phase pass.
- Understanding score meets the threshold.
- The application still works end-to-end.
- Git commit(s) are created.
- No known critical regression remains.

---

# 6. Progress Rule

When working on FlowOps:

### Always know these three things:

**Current Phase**

The phase actively being implemented.

**Current Task**

The smallest task currently being worked on.

**Next Task**

The next task after successful verification.

Never keep the next 20 coding tasks in your head.

---

# 7. Stop Rule

When a phase becomes too large, split the phase task into smaller tasks.

Do not respond to being stuck by:

- redesigning the whole project
- switching technologies
- starting a new project
- adding unrelated features
- rewriting working code

Instead:

1. Identify the exact blocker.
2. Reduce it to the smallest reproducible problem.
3. Understand the concept.
4. Fix it.
5. Continue the current phase.

---

# 8. Definition of Done for the Entire Project

FlowOps is complete when:

- Core product requirements are implemented.
- The main application is deployed.
- Tests cover critical business rules.
- Docker works.
- CI/CD works.
- The developer understands the architecture.
- The developer can independently implement a small feature.
- The developer can debug a production-like issue.
- The developer can explain key design decisions in an interview.
- The GitHub repository clearly demonstrates professional development practices.
