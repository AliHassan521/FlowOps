# FlowOps Architecture

## 1. Architectural Goal

FlowOps will use a **modular layered monolith** architecture.

The application should remain simple enough for a solo developer while still demonstrating professional software-engineering practices.

We are deliberately NOT starting with microservices.

The architecture must support future growth without introducing unnecessary complexity.

---

## 2. High-Level Architecture

```text
                    Angular Frontend
                           |
                           | HTTPS / REST
                           v
                 ASP.NET Core Web API
                           |
          +----------------+----------------+
          |                |                |
          v                v                v
     Application       SignalR Hub     Background Worker
        Layer                              |
          |                                |
          v                                |
       Domain                              |
        Rules                              |
          |                                |
          v                                v
     Infrastructure ------------------> Redis
          |
          v
      SQL Server
```

Optional later integrations:

```text
GitHub Webhooks --> ASP.NET Core API --> Release/Activity processing

AI Provider      --> ASP.NET Core API --> Incident analysis
```

---

## 3. Architectural Style

Use a layered/modular monolith with clear boundaries.

Recommended conceptual layers:

```text
API
Application
Domain
Infrastructure
Contracts/Common
```

### API

Responsible for:

- HTTP endpoints
- Request/response handling
- Authentication configuration
- Authorization policies
- SignalR endpoints/hubs
- API versioning strategy if needed later

Controllers/endpoints should remain thin.

### Application

Responsible for:

- Use cases
- Business orchestration
- Application services
- Validation
- DTO mapping where appropriate
- Transaction coordination

### Domain

Responsible for:

- Core business concepts
- Entities
- Value objects where justified
- Domain rules
- State transition rules
- Domain-level abstractions

The domain should not depend on infrastructure details.

### Infrastructure

Responsible for:

- EF Core
- SQL Server access
- Redis
- External services
- Repository implementations when repositories are actually useful
- Background infrastructure
- Email/notification infrastructure if introduced

### Contracts/Common

Contains carefully shared items such as:

- API contracts
- Shared constants
- Common result/error representations
- Cross-cutting abstractions that truly belong here

Avoid creating a dumping-ground project.

---

## 4. Frontend Architecture

Angular will use standalone components.

Conceptual structure:

```text
src/app/
    core/
    shared/
    features/
        auth/
        dashboard/
        projects/
        work-items/
        incidents/
        releases/
        notifications/
    layout/
```

### Core

For singleton/application-wide services:

- Auth
- HTTP configuration
- route guards
- interceptors
- configuration
- global application services

### Shared

For genuinely reusable UI/components/pipes/utilities.

Do not place feature-specific logic here.

### Features

Each business area owns its screens and feature logic.

This prevents a giant Angular folder and makes the application easier to understand.

---

## 5. Authentication Architecture

```text
Angular Login Form
       |
       v
POST /api/auth/login
       |
       v
Auth Endpoint
       |
       v
Authentication Service
       |
       +----> User data
       |
       +----> Password verification
       |
       +----> Token creation
       |
       v
JWT / refresh-token strategy
       |
       v
Angular authenticated session
```

All protected API requests must go through the normal Angular authentication mechanism.

Authorization must be enforced on the server even if the UI hides unavailable controls.

Access tokens are short-lived JWTs. Refresh tokens are cryptographically random opaque values
stored only as SHA-256 hashes in SQL Server and delivered through an HttpOnly cookie. Refresh
tokens rotate on every use and are revoked on logout.
Refresh and logout requests require an Origin matching the configured frontend
origin to prevent cross-origin use of the cookie-backed session.

---

## 6. Multi-Tenant Boundary

The organization is the primary tenant boundary.

Every organization-owned resource must have a clear relationship to the organization.

The server must determine authorization from authenticated identity and resource ownership/membership rather than trusting organization identifiers supplied by the client.

### Implemented organization/project access model

- `OrganizationMembership` is keyed by `(OrganizationId, UserId)` and stores the active flag and role for that organization.
- A user's organization role is resolved from the database membership on each organization/project operation. Global JWT role claims do not grant access to a tenant's resources.
- The shared `Roles` table is the role catalog; the same user can have a different role in another organization.
- Organization- and project-scoped queries include the requested organization ID and active membership. Cross-tenant requests from nonmembers return not found to avoid exposing resource existence; role restrictions for an active member return forbidden.
- Only Admin and TeamLead can create or update projects; only Admin can archive them. All active organization members can read projects.
- Organization and project keys/slugs have database unique indexes. Project keys are unique per organization.
- Organization, membership, and project mutations write audit records in the same EF Core save/transaction as the business change. The application DbContext rejects audit-event updates and deletions, preserving append-only history through normal persistence paths.
- Organization and project deactivation is soft; their rows and audit history remain in the database. Access still follows the active organization/membership rules.

Only an organization Admin can change memberships. Requested roles are validated against the seeded role catalog; client-supplied role names never bypass the database membership check.

Example:

```text
User
  |
  +--> Organization
          |
          +--> Projects
          |      |
          |      +--> Work Items
          |
          +--> Incidents
          |
          +--> Releases
```

---

## 7. Work Item Workflow Architecture

Work-item status changes must go through a controlled application/business workflow.

```text
Current State
     |
     v
Transition Request
     |
     v
Permission Check
     |
     v
Transition Rule Check
     |
     +---- invalid ---> reject
     |
     v
Persist New State
     |
     v
Audit Event
     |
     v
Notification/Event
     |
     v
SignalR update where appropriate
```

Do not allow arbitrary direct status mutation from multiple places.

---

## 8. Background Processing Architecture

Recurring jobs will initially use ASP.NET Core hosted services/background services.

Example:

```text
Background Worker
       |
       v
Find active SLA records
       |
       +--> approaching deadline?
       |        |
       |        +--> notification
       |
       +--> breached?
                |
                +--> mark breach
                +--> audit
                +--> notification
                +--> real-time update
```

Background jobs must be idempotent where practical.

The same record must not generate duplicate processing merely because a worker restarted.

---

## 9. Real-Time Architecture

SignalR is used only for events where immediate client updates provide value.

Example:

```text
Work Item Update
      |
      v
Application Service
      |
      +--> Database
      |
      +--> Audit
      |
      +--> Notification
      |
      +--> SignalR event
               |
               v
        Connected clients
```

Real-time events should not become the source of truth.

SQL Server remains the source of persistent state.

---

## 10. Caching Architecture

Redis is optional and introduced only after the underlying feature works correctly without it.

Good candidates:

- Frequently requested dashboard aggregates
- Short-lived lookup/configuration data
- Rate limiting where appropriate

Do not cache everything.

Every cache should have:

- clear key naming
- expiration policy
- invalidation strategy
- fallback to the primary data store

---

## 11. Data Architecture

SQL Server is the primary relational store.

Use:

- normalization where appropriate
- explicit relationships
- indexes based on actual query needs
- timestamps
- auditability
- transactions for multi-step state changes

EF Core is the default data-access path.

Use raw SQL/stored procedures only for justified scenarios.

---

## 12. Error Handling

The API should use a consistent error response model.

Expected categories:

- Validation errors
- Authentication errors
- Authorization errors
- Not found
- Conflict/business rule failures
- Unexpected server errors

Do not expose stack traces or internal exception details to clients in production-style responses.

---

## 13. Observability Architecture

The application should progressively support:

- Structured logs
- Request correlation
- Health checks
- Metrics
- Distributed tracing where justified

Observability is an enhancement layer, not a prerequisite for the first working release.

---

## 14. Testing Architecture

Test boundaries according to risk.

```text
Business rules
   -> Unit tests

API + important flows
   -> Integration tests

Browser-level critical flows
   -> Optional end-to-end tests later
```

The most important business rules must not depend exclusively on manual testing.

---

## 15. Deployment Architecture

Local development:

```text
Angular
.NET API
SQL Server
Redis (when introduced)
```

Containerized later:

```text
docker compose
    |
    +--> frontend
    +--> backend
    +--> database
    +--> redis
```

CI/CD:

```text
Git push
   |
   v
GitHub Actions
   |
   +--> restore
   +--> build
   +--> test
   +--> package/containerize
   |
   v
Deployment
```

---

## 16. Architecture Principles

1. Prefer the simplest architecture that satisfies the requirement.
2. Do not introduce infrastructure before a real use case exists.
3. Keep controllers thin.
4. Keep business rules out of controllers and UI.
5. Keep domain logic independent from infrastructure.
6. Keep database access intentional and testable.
7. Prefer explicit code over clever abstractions.
8. Do not create interfaces or patterns without a useful reason.
9. Avoid premature microservices.
10. Every significant architectural change must be documented and justified.
