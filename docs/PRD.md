# FlowOps Product Requirements Document

## 1. Product Overview

**Product Name:** FlowOps

**Product Type:** B2B Engineering Operations Platform

**Primary Goal:**  
Build a realistic software-engineering platform that demonstrates strong .NET backend, Angular frontend, SQL Server, real-time communication, background processing, testing, containerization, and CI/CD skills without becoming an oversized enterprise project.

FlowOps is intended for software teams to manage engineering work, incidents, service-level deadlines, notifications, and software releases in one place.

---

## 2. Problem Statement

Software teams often use separate tools for:

- Project/work tracking
- Bug management
- Incident tracking
- SLA monitoring
- Team notifications
- Release tracking
- Audit/history

FlowOps combines these workflows into one focused platform.

The product should demonstrate real business rules and engineering problems rather than being a simple CRUD application.

---

## 3. Target Users

### Organization Admin
Manages the organization, users, roles, projects, and system configuration.

### Team Lead
Manages projects, assigns work, monitors SLAs, incidents, and team performance.

### Developer
Works on assigned tasks/bugs, comments, updates status, and participates in incident resolution.

### QA
Reviews work items, updates QA status, and participates in verification.

### Viewer
Read-only access to permitted projects, dashboards, incidents, and releases.

---

## 4. Core Product Areas

### 4.1 Authentication and Authorization

The system must support:

- User registration/invitation where appropriate
- Login/logout
- Password hashing
- JWT authentication
- Refresh-token strategy
- Role-based authorization
- Organization-aware access control
- Account activation/deactivation

### 4.2 Organizations

An organization is the top-level tenant boundary.

Each organization can contain:

- Users
- Teams
- Projects
- Work items
- Incidents
- Releases
- Notifications
- Audit records

Users must only access resources they are authorized to access within their organization.

### 4.3 Projects

A project contains:

- Name
- Key/code
- Description
- Status
- Owner
- Team members
- Work items
- Incidents
- Releases

### 4.4 Work Items

Work items include:

- Task
- Bug

Each work item can contain:

- Title
- Description
- Priority
- Status
- Reporter
- Assignee
- Project
- Due date
- SLA deadline
- Tags
- Comments
- History/audit information
- Created/updated timestamps

### 4.5 Workflow

A work item should follow controlled state transitions.

Example:

`New -> Triaged -> Assigned -> In Progress -> Code Review -> QA -> Resolved -> Closed`

The system must reject invalid transitions and enforce role/business rules where necessary.

### 4.6 Notifications

Notifications are generated for important events such as:

- Work assigned
- Status changed
- Mention/comment
- SLA approaching breach
- SLA breached
- Incident assigned
- Release status changed

Notifications should be visible in the Angular application and updated in real time where applicable.

### 4.7 SLA Management

Selected work items and incidents can have SLA rules.

The system should:

- Calculate deadlines
- Track remaining time
- Detect approaching deadlines
- Detect breaches
- Record breach history
- Notify responsible users
- Update dashboards

### 4.8 Incident Management

Incidents represent operational problems.

An incident includes:

- Incident number
- Title
- Description
- Severity
- Status
- Affected service
- Owner
- Start time
- End time
- SLA information
- Timeline/events
- Comments
- Resolution/root cause
- Audit history

Example lifecycle:

`Open -> Investigating -> Mitigated -> Resolved -> Closed`

### 4.9 Release Tracking

Teams can track software releases.

A release can contain:

- Application
- Version
- Environment
- Release status
- Deployment time
- Notes
- Linked work items
- Linked incidents

Example status flow:

`Draft -> Approved -> Deploying -> Successful / Failed`

### 4.10 Dashboard

The dashboard should show useful operational information such as:

- Open work items
- Critical items
- SLA risks
- SLA breaches
- Active incidents
- Recent releases
- Team workload
- Resolution metrics

The dashboard should provide meaningful aggregates instead of merely listing database records.

---

## 5. Real-Time Requirements

Use ASP.NET Core SignalR where real-time communication provides value.

Real-time updates should include:

- Notifications
- Work item changes where appropriate
- Incident updates
- Dashboard-sensitive events where appropriate

The system must avoid unnecessary polling when a push-based event is more appropriate.

---

## 6. Background Processing Requirements

Use ASP.NET Core hosted/background services for recurring processing.

Examples:

- SLA monitoring
- Deadline checks
- Scheduled notification generation
- Cleanup of expired temporary data if needed

Background work must not block normal HTTP requests.

---

## 7. Performance and Data Requirements

The system must demonstrate sound SQL Server practices.

Requirements include:

- Proper relational design
- Primary/foreign keys
- Appropriate indexes
- Pagination
- Filtering and sorting
- Avoiding `SELECT *`
- Transaction usage where required
- Efficient queries for dashboard/reporting use cases

EF Core is the primary data-access technology.

Raw SQL/stored procedures may be used deliberately where they provide a clear reason such as reporting, performance, or demonstrating SQL expertise.

---

## 8. Security Requirements

The application must:

- Hash passwords securely
- Never store plain-text passwords
- Validate all external input
- Enforce authorization on the server
- Protect sensitive endpoints
- Avoid exposing internal implementation details
- Never commit secrets
- Use configuration/environment variables for secrets
- Implement appropriate CORS policy
- Handle authentication failures safely

---

## 9. Testing Requirements

The project should contain:

### Unit Tests
For business rules such as:

- Workflow transitions
- SLA calculation
- Authorization-sensitive business logic
- Status changes

### Integration Tests
For:

- API + database behavior
- Authentication flows
- Important endpoint workflows

Testing should be added progressively rather than postponed until the end.

---

## 10. Deployment Requirements

The project should eventually support:

- Dockerized local environment
- Docker Compose for local multi-service setup
- GitHub Actions CI pipeline
- Automated build
- Automated tests
- Deployment to a free/low-cost compatible hosting option when practical

No paid infrastructure is required for development.

---

## 11. Optional Advanced Features

These features are intentionally secondary and must not block the core product:

### GitHub Integration
- Repository connection
- Webhook handling
- Commit/PR/release linkage

### AI Incident Analysis
Use AI to summarize incident information and suggest investigation steps from available incident data.

### Redis
Use for clearly justified caching/rate-limiting scenarios.

### Observability
Add structured logging, health checks, metrics, and tracing.

### .NET Aspire
Consider only after the core system works and the added complexity is justified.

---

## 12. Non-Goals

The initial product will NOT include:

- Microservices architecture
- Kubernetes
- Complex event sourcing
- Kafka
- Multiple message brokers
- Mobile application
- Payment processing
- Large-scale enterprise IAM
- Complex workflow designer
- Full production-grade monitoring SaaS
- Highly autonomous AI agents

The goal is a strong, defendable portfolio project for a developer with approximately 1–2 years of experience.

---

## 13. Success Criteria

FlowOps is considered portfolio-ready when:

1. A new user can authenticate.
2. An authorized user can create/access a project.
3. Work items can be created, assigned, updated, and transitioned using valid workflow rules.
4. Important actions are recorded in audit history.
5. Notifications work.
6. Real-time updates work for selected scenarios.
7. SLA monitoring works.
8. Incidents can be managed through their lifecycle.
9. Releases can be tracked.
10. Dashboard metrics are useful and backed by real queries.
11. Important business logic has automated tests.
12. The project can be run reliably using documented setup steps.
13. The repository demonstrates professional Git history and CI/CD.
14. The developer can explain, debug, extend, and defend the main design decisions in an interview.
