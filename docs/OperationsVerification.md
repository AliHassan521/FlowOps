# CI and local operations verification — 2026-11-03

The remaining core product modules are implemented. This milestone adds reproducible development infrastructure and build/test gates; it does not configure Internet-facing hosting.

## Local evidence

- All 177 backend tests passed in Release before this operations slice. A final regression rerun also covers current assignments remaining visible after a member changes to Viewer; read-only permission changes must not silently hide workload.
- 93 Angular tests pass, including seven origin-selection cases. Packaged localhost builds now use their proxy's `/api`, avoiding an unrelated development API on port 5144. Angular production build passes at 461.48 kB initial size.
- Complete solution (including the new migration runner) builds in Release with zero warnings/errors.
- The standalone migrator created a fresh `FlowOpsCompletionCleanValidation` database with all 13 migrations and five seeded roles. A second invocation succeeded without schema changes. Missing/master target guards and non-Compose login-provisioning guards returned failure without exposing connection information.
- Secret initializer creates independent random values and refuses overwrite. `.env` is ignored by Git and excluded from Docker build context. Compose's actual CLI validates `docker compose config --quiet`; Prettier parses/validates workflow and Compose YAML.
- The NuGet audit gate accepts a real clean report; four automated gate tests cover direct/transitive advisories, unavailable-feed warnings, and incomplete output. Runtime npm and direct/transitive NuGet audits report no known vulnerabilities. The previously recorded 16 moderate development-tool findings remain open.
- Local Linux-container execution is blocked: `wsl --status` reports WSL is not installed; Docker Desktop remains stopped and its engine endpoint cannot serve builds. No OS/virtualization configuration was changed. Local container build/run success is not claimed.

## CI verification status

The workflow is configured to execute backend and frontend regression/build/security gates and a fresh Linux Compose/SQL Server smoke test on GitHub. The first remote run will be checked after the reviewed workflow commit is published, and its result recorded here. Until then, container execution remains unverified.

## Remaining release work

An identified host, HTTPS/trusted proxy configuration, production identities/secrets, image scanning, load/capacity testing, backup/restore rehearsal, monitoring and deployment/rollback practice remain open. Optional Redis/GitHub/AI integrations have no current core-product dependency and were not added. Phase learning sign-offs remain open.
