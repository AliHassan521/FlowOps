# FlowOps Development Rules

## 1. Purpose

This document is the operating contract for:

- The developer
- AI coding agents
- Review/mentoring sessions
- Future contributors

These rules exist to prevent scope creep, unsafe AI changes, architecture drift, and passive code copying.

---

# 2. General Development Rules

1. Build only the current phase unless a dependency requires otherwise.
2. Work on one focused task at a time.
3. Never rewrite working code without a clear reason.
4. Never add a major dependency without understanding why it is needed.
5. Prefer the simplest solution that meets the requirement.
6. Do not add technology merely because it is popular.
7. Do not introduce microservices into the initial architecture.
8. Do not create abstractions without a real use case.
9. Keep the application understandable to a developer with approximately 1–2 years of experience.
10. Keep business logic explicit and easy to follow.

---

# 3. AI Coding Rules

AI is a development assistant, not the project owner.

Before making a non-trivial change, the AI must:

1. Read PRD.md.
2. Read Architecture.md.
3. Read Design.md.
4. Read Phases.md.
5. Read Rules.md.
6. Identify the current phase.
7. Identify the exact task.
8. State which files it expects to modify.
9. Explain the implementation approach briefly.

AI must NOT:

- Rewrite unrelated files.
- Change architecture without justification.
- Add large libraries unnecessarily.
- Start future phases early.
- Remove working functionality to simplify its own work.
- Invent requirements not present in the project documentation.
- Commit secrets.
- Hide breaking changes.
- Claim a feature is complete without verification.

---

# 4. AI Task Size Rule

Preferred AI task size:

```text
One feature slice
or
One logical implementation step
```

Good:

> Create the Users entity and EF Core configuration according to the current database design.

Bad:

> Build the entire authentication system.

Very bad:

> Build the whole application.

---

# 5. Explain-Before-Implement Rule

For a meaningful task, AI should provide:

```text
Goal
Files to change
Approach
Testing method
```

Then implement.

The explanation should be short and practical.

---

# 6. User Understanding Rule

After AI-assisted implementation of an important concept, the developer must be able to answer:

- What did we build?
- Why did we build it this way?
- What happens at runtime?
- What can fail?
- How would I change it?
- How would I debug it?

If the developer cannot answer these, the feature is not fully learned even if the code works.

---

# 7. No-AI Challenge Rule

For every major concept, perform a small independent challenge.

Examples:

### Authentication

Implement a small protected endpoint without looking at the project implementation.

### Workflow

Add a new transition independently.

### SQL

Write one aggregate query independently.

### SignalR

Create one simple event flow independently.

### Background Processing

Create a small scheduled worker independently.

### Testing

Write one business-rule test independently.

### Docker

Write a minimal Dockerfile independently.

The purpose is not to reproduce production code exactly.

The purpose is to prove understanding.

---

# 8. Backend Rules

1. Use ASP.NET Core.
2. Use dependency injection.
3. Keep controllers/endpoints thin.
4. Keep business logic in application/domain services.
5. Keep infrastructure details out of domain logic.
6. Use async/await for I/O-bound operations.
7. Validate external input.
8. Return appropriate HTTP status codes.
9. Do not expose persistence entities casually through the API.
10. Keep authentication and authorization server-enforced.

---

# 9. Database Rules

1. Use SQL Server as the primary relational store.
2. Every table needs an intentional primary key.
3. Foreign keys must be explicit.
4. Use meaningful names.
5. Avoid `SELECT *`.
6. Add indexes because of real query patterns.
7. Do not add random indexes.
8. Use transactions for operations that must be atomic.
9. Use pagination for potentially large collections.
10. Keep database constraints strong.
11. EF Core is the default access path.
12. Raw SQL/stored procedures require a clear justification.

---

# 10. Angular Rules

1. Use standalone components.
2. Use Reactive Forms.
3. Use feature-based organization.
4. Keep components focused on UI concerns.
5. Put API calls in services.
6. Keep authorization enforced on the backend.
7. Do not introduce a large state-management library unless the application truly requires it.
8. Avoid unnecessary third-party UI frameworks.
9. Keep observable/data flows understandable.
10. Do not duplicate business rules from the backend unnecessarily.

---

# 11. API Rules

1. Use predictable resource-oriented endpoint names.
2. Validate requests.
3. Enforce authentication/authorization.
4. Use consistent response/error formats.
5. Support pagination for collection endpoints.
6. Avoid returning unnecessary fields.
7. Use appropriate status codes.
8. Keep controllers small.
9. Document non-obvious behavior.
10. Do not expose stack traces to clients.

---

# 12. Security Rules

1. Never store plain-text passwords.
2. Never commit secrets.
3. Never place secrets in source code.
4. Use configuration/environment variables for sensitive values.
5. Validate authorization on the server.
6. Respect organization boundaries.
7. Do not trust organization/user identifiers from the client without authorization checks.
8. Validate external inputs.
9. Use secure token handling appropriate to the chosen authentication design.
10. Avoid exposing internal exception details.

---

# 13. Real-Time Rules

1. Use SignalR only where real-time behavior adds value.
2. Persistent state belongs in the database.
3. Real-time messages are notifications of state changes, not the source of truth.
4. Client reconnect behavior must be considered.
5. Do not refresh the entire application unnecessarily for every event.

---

# 14. Background Processing Rules

1. Background work must not block HTTP requests.
2. Use cancellation tokens.
3. Make recurring operations idempotent where possible.
4. Avoid duplicate notifications/side effects.
5. Use appropriate service scopes.
6. Process bounded batches where necessary.
7. Handle failures safely.
8. Log important worker failures.

---

# 15. Redis Rules

1. Redis is optional until a justified use case exists.
2. Do not cache everything.
3. Define expiration policy.
4. Define invalidation behavior.
5. SQL Server remains the source of truth.
6. A cache failure should not unnecessarily destroy core functionality unless the specific feature explicitly requires it.

---

# 16. Testing Rules

1. Test important business rules.
2. Test authorization boundaries.
3. Test important error paths.
4. Prefer deterministic tests.
5. Do not write tests only for coverage numbers.
6. Keep unit tests fast.
7. Use integration tests for behavior that depends on real infrastructure boundaries.
8. CI must run relevant tests before deployment.

---

# 17. Git Rules

Use meaningful commits.

Examples:

```text
feat: add project creation
feat: implement jwt login
fix: prevent duplicate sla breach notifications
test: add workflow transition tests
refactor: separate notification service
docs: update architecture
```

Avoid:

```text
update
changes
test
final
final2
working
```

Commit working milestones frequently.

---

# 18. Documentation Rules

Update the relevant documentation when:

- Architecture changes
- Important design decisions change
- A phase changes scope
- A rule changes
- A significant feature behaves differently from the original design

Do not allow the documentation to drift significantly from the implementation.

---

# 19. Scope Control Rules

Not every good idea belongs in FlowOps.

Before adding a feature ask:

1. Is it required by PRD.md?
2. Does it help demonstrate a valuable engineering skill?
3. Can it be implemented without destabilizing the current phase?
4. Does it justify its complexity?

If the answer is mostly no, put the idea aside.

Do not expand the scope during the core build.

---

# 20. Dependency Rules

Before adding a library, answer:

- Why do we need it?
- Can the built-in platform solve the problem?
- Does it add meaningful value?
- Is it actively maintained?
- Does it increase architectural complexity?
- Can the team understand it?

Prefer built-in .NET/Angular features when they are sufficient.

---

# 21. Debugging Rules

When something breaks:

1. Reproduce the problem.
2. Read the exact error.
3. Identify the failing layer.
4. Check logs.
5. Inspect request/response data.
6. Inspect database state if relevant.
7. Form a hypothesis.
8. Test the hypothesis.
9. Make the smallest safe fix.
10. Add a regression test when appropriate.

Do not immediately ask AI to rewrite everything.

---

# 22. Learning Rules

A feature is not considered learned just because AI wrote it.

The developer should progressively reach:

```text
Build       0-3
Understand  0-3
Debug       0-3
Defend      0-3
```

Target:

```text
>= 2 in all categories
```

Critical interview topics should reach:

```text
3 where possible
```

---

# 23. "Stuck" Rule

When stuck:

Do NOT:

- Start another project.
- Change stacks.
- Redesign the architecture.
- Delete everything.
- Add random technologies.

Instead:

1. Identify the smallest blocker.
2. Ask for a conceptual explanation.
3. Reduce the problem.
4. Test one assumption.
5. Fix one thing.
6. Continue.

---

# 24. Completion Rule

A feature is complete only when:

- It works.
- Its main failure paths work correctly.
- Relevant tests exist.
- It follows the architecture.
- It follows the rules.
- The developer understands it.
- Git history records the milestone.

---

# 25. Final Principle

The goal of FlowOps is not:

> "Use AI to produce a large amount of code."

The goal is:

> "Use AI to help build a professional application while becoming capable of understanding, debugging, extending, and defending the code."

AI may accelerate implementation.

It must not replace understanding.
