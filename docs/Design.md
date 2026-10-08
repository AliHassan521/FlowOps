# FlowOps Design

## 1. Purpose

This document describes how the important features of FlowOps behave internally.

Architecture.md explains the major system boundaries.

Design.md explains the important feature flows, rules, states, and implementation behavior.

---

## 2. Authentication Design

### Login Flow

```text
User enters email/password
        |
        v
Angular login form
        |
        v
POST /api/auth/login
        |
        v
Validate request
        |
        v
Load user
        |
        v
Verify password hash
        |
        +---- invalid ---> authentication failure
        |
        v
Check account status
        |
        v
Generate access token
        |
        v
Generate/rotate refresh token according to implementation
        |
        v
Return authentication response
```

### Rules

- Passwords are never stored in plain text.
- Authentication must be server-side.
- Protected APIs must validate the token.
- Authorization must be checked independently from authentication.
- Expired/invalid tokens must not grant access.

---

## 3. Authorization Design

Authorization operates at multiple levels:

### Role Level

Example:

```text
Organization Admin
Team Lead
Developer
QA
Viewer
```

### Resource Level

A user may be authenticated but still forbidden from a specific project or organization resource.

### Action Level

Examples:

- Only authorized users can manage members.
- Only permitted roles can change certain workflow states.
- Only permitted users can close incidents.
- Only authorized users can approve releases.

The server must enforce these rules.

---

## 4. Project Design

### Project Creation

```text
Create Project
    |
    v
Validate name/key
    |
    v
Validate organization access
    |
    v
Create project
    |
    v
Assign owner/team membership where applicable
    |
    v
Audit event
```

### Project Constraints

- Project key must be unique within an organization.
- Project must belong to exactly one organization.
- Access must respect organization boundaries.
- Deletion should be considered carefully; archival is preferred for important records.

---

## 5. Work Item Design

A work item can be a:

- Task
- Bug

### Required Core Data

```text
Id
ProjectId
Type
Title
Description
Priority
Status
ReporterId
AssigneeId
DueDate
SlaDeadline
CreatedAt
UpdatedAt
```

### Creation Flow

```text
Client
  |
  v
Validate request
  |
  v
Check project access
  |
  v
Validate assignee
  |
  v
Determine initial state
  |
  v
Calculate SLA if applicable
  |
  v
Persist work item
  |
  v
Create audit event
  |
  v
Create notification where required
```

---

## 6. Workflow Design

### Example State Machine

```text
New
 |
 v
Triaged
 |
 v
Assigned
 |
 v
In Progress
 |
 v
Code Review
 |
 v
QA
 |
 v
Resolved
 |
 v
Closed
```

Possible transition rules must be explicitly defined in code.

Examples:

- New -> Triaged: allowed for authorized users.
- Triaged -> Assigned: requires assignment.
- Assigned -> In Progress: requires an assignee.
- In Progress -> Code Review: requires work completion criteria.
- Code Review -> QA: requires review approval where configured.
- QA -> Resolved: requires successful validation.
- Resolved -> Closed: allowed for authorized users.
- Closed -> In Progress: only through an explicit reopen action.

The exact rules can evolve, but there must be one clear workflow mechanism.

---

## 7. Audit Design

Audit records should capture important state-changing actions.

Example:

```text
Actor
Action
EntityType
EntityId
Timestamp
OldValue/summary where appropriate
NewValue/summary where appropriate
Metadata
```

Audit logging should focus on meaningful business events rather than every internal method call.

Examples:

- User role changed
- Work item assigned
- Work item status changed
- SLA breached
- Incident status changed
- Release approved

---

## 8. Notification Design

### Notification Lifecycle

```text
Business Event
     |
     v
Determine recipients
     |
     v
Create notification
     |
     +--> Persist
     |
     +--> SignalR push
```

A notification should be persisted before or as part of the business operation according to the transaction strategy.

A real-time push must not be treated as the permanent record.

---

## 9. SLA Design

### SLA Data

A record may include:

```text
SlaPolicy
SlaDuration
StartedAt
Deadline
WarningThreshold
BreachedAt
Status
```

### Example

```text
Critical Bug
SLA = 4 hours

Created: 10:00
Deadline: 14:00
Warning threshold: 13:00
```

The background worker should identify:

- Items approaching deadline
- Items that have breached
- Items already processed

### Duplicate Processing Prevention

A breach must be marked/recorded so repeated worker executions do not create duplicate notifications or audit entries.

---

## 10. Incident Design

### Incident State Machine

```text
Open
  |
  v
Investigating
  |
  v
Mitigated
  |
  v
Resolved
  |
  v
Closed
```

### Incident Timeline

Each important incident event should be recorded.

Example:

```text
10:42 Incident created
10:44 Developer assigned
10:51 Investigation started
11:03 Root cause identified
11:15 Fix deployed
11:22 Incident resolved
```

### Resolution Data

Resolved incidents should capture:

- Resolution summary
- Root cause where known
- Resolved by
- Resolved time

---

## 11. Release Design

A release is associated with an application/project and environment.

### Release Flow

```text
Draft
  |
  v
Approved
  |
  v
Deploying
  |
  +----> Successful
  |
  +----> Failed
```

Important releases should retain:

- Version
- Environment
- Change notes
- Linked work items
- Deployment status
- Timestamps
- Actor information

---

## 12. Dashboard Design

The dashboard should be built from meaningful aggregates.

Example widgets:

```text
Open Work Items
Critical Items
SLA Risks
SLA Breaches
Active Incidents
Recent Releases
Team Workload
Resolution Trend
```

Avoid loading every underlying record to calculate each metric in application memory.

Prefer efficient database-side aggregation.

---

## 13. Real-Time Design

SignalR events should use meaningful event names.

Conceptual examples:

```text
workItemUpdated
notificationCreated
incidentUpdated
releaseUpdated
slaBreached
```

Clients should update only the affected state where practical instead of blindly refreshing the entire application.

---

## 14. Background Worker Design

The worker should:

1. Start with the application.
2. Run on a controlled interval.
3. Create a bounded processing scope.
4. Query only relevant records.
5. Process safely.
6. Record important outcomes.
7. Handle failures without crashing the whole API.
8. Avoid duplicate side effects.

The worker interval and batch size should be configurable.

---

## 15. Caching Design

For a dashboard cache:

```text
Request
  |
  v
Check Redis
  |
  +---- hit ---> return cached result
  |
  v
Query SQL Server
  |
  v
Store result with expiration
  |
  v
Return result
```

Important writes that affect cached metrics should invalidate the affected cache keys when necessary.

---

## 16. API Design Rules

API endpoints should:

- Use predictable resource naming
- Return appropriate HTTP status codes
- Validate inputs
- Respect authorization
- Support pagination for collections
- Avoid exposing database entities directly when that creates coupling
- Return useful validation/error information
- Keep business logic outside controllers

---

## 17. Frontend Design Rules

Angular feature pages should generally follow:

```text
Component
   |
   v
Feature Service
   |
   v
HTTP API
```

For state that genuinely needs cross-component coordination, use the simplest suitable RxJS-based approach before introducing more complex state-management libraries.

Forms should use Reactive Forms.

---

## 18. Error Handling Design

Frontend should distinguish:

- Validation errors
- Authentication failures
- Authorization failures
- Not found
- Business conflicts
- Unexpected server failures

User-facing messages should be understandable.

Technical details should remain in logs rather than being displayed to users.

---

## 19. AI Feature Design

The optional AI feature is **Incident Analysis**.

Input:

- Incident title
- Description
- Timeline
- Comments
- Related error/log text when available

Output:

```text
Summary
Possible causes
Evidence
Suggested investigation steps
Suggested next actions
```

AI output is advisory.

It must not automatically change production state or close incidents.

The original incident data remains the source of truth.

---

## 20. Design Quality Rules

1. Prefer explicit workflows over unrestricted state mutation.
2. Keep business rules centralized.
3. Keep real-time events secondary to persisted state.
4. Make background processing idempotent.
5. Use database aggregation for large summaries.
6. Avoid unnecessary abstraction.
7. Design for testability.
8. Keep optional features from blocking the core product.
9. Every non-trivial feature should have a clear happy path and failure path.
10. The final implementation must remain understandable by a developer with 1–2 years of experience.
