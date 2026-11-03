# Running and verifying FlowOps

## Local container environment

Prerequisites: Docker Desktop with a running Linux engine (on Windows, WSL2/virtualization must already be configured), Docker Compose v2, and PowerShell 7. The SQL Server Developer image is for development/testing; starting it accepts Microsoft's SQL Server container EULA. This stack is intentionally loopback-only HTTP development. It is not an Internet-facing production deployment.

From the repository root:

```powershell
./scripts/Initialize-LocalEnvironment.ps1
docker compose config --quiet
docker compose up --build -d
```

Open `http://localhost:8080`. Readiness is `http://localhost:8080/health`. Only the web port is published, bound to `127.0.0.1`; SQL Server and the API are internal. The proxy serves Angular deep links and forwards API/SignalR traffic. Packaged localhost builds use same-origin `/api`; only `http://localhost:4200` uses the separate development API on port 5144.

The initializer creates three independent random secrets in ignored `.env` and refuses to overwrite existing credentials. Keep that file private and retain it while the SQL named volume exists. Do not commit it or pass `docker compose config` output to shared logs: expanded configuration includes credentials. Use `--quiet` for validation.

Startup order: SQL health → explicit migration/provisioning job → API → web. The API runs as a non-root user with a separate SQL login granted data-reader/data-writer roles, with explicit denials for rewriting audit events or migration history. SQL memory is bounded for the local stack. It does not receive SA credentials or migrate schema at startup. The provisioner can create that local runtime login only for the exact `sql,1433` / `FlowOps` Compose target and explicit local provisioning flag. API/web filesystems are read-only with writable temporary directories; their Linux capabilities are dropped.

```powershell
docker compose ps
docker compose logs --tail=100 api migrate
docker compose down
```

`down` retains the named SQL volume. The repository's scripts never delete database volumes. Before any manual reset, make a backup and verify exactly which project/volume is being removed.

The base images track supported .NET 10, Node 24, nginx stable and SQL Server 2022 channels. CI rebuilds them; Dependabot reviews updates. These channel tags are mutable: capture verified image digests and scan the resulting images before a production release rather than assuming an old build stays patched.

## Explicit SQL migrations without containers

Inspect the target and migration SQL; back up a populated database before updating it. Existing native local development can still use `dotnet ef database update` as described in README. The standalone tool also supports a reviewed connection from environment configuration:

```powershell
$env:ConnectionStrings__DefaultConnection = '<reviewed connection string from a secret store>'
dotnet run -c Release --project backend/src/FlowOps.Migrator
Remove-Item Env:ConnectionStrings__DefaultConnection
```

Without local provisioning variables, the tool only applies EF migrations. It rejects an empty/master database target, exits nonzero on failure and keeps connection strings/SQL payloads out of its console errors. A migration identity needs schema permissions; an ordinary application identity should not have them. The Phase 8 downgrade guard refuses to discard deployment history or shrink long audit records; use a reviewed backup/restore plan after data has been written.

## Verification commands

```powershell
dotnet build backend/FlowOps.slnx -c Release --warnaserror
dotnet test backend/FlowOps.slnx -c Release --no-build
cd frontend
npm install --global npm@11.21.1
npm ci
npm test -- --watch=false
npm run build
npm audit --omit=dev
```

On an isolated running Compose stack:

```powershell
$env:FLOWOPS_API_URL = 'http://localhost:8080'
$env:FLOWOPS_ORIGIN = 'http://localhost:8080'
node backend/tests/phase8-smoke.cjs
```

This smoke test registers synthetic accounts, creates one synthetic tenant, exercises concurrent writes and all related workflows, then archives that tenant. It permits localhost targets only. Do not point it at a real development data set or production service. Phase 7's longer SignalR/SLA smoke script additionally exercises a real two-minute warning/breach; use its documented native development API setup.

## CI gates and release limits

`.github/workflows/ci.yml` runs on main/completion branches and pull requests, with read-only repository permissions, pinned official action revisions and no deployment credentials. Jobs build/test the backend, test/build Angular, gate runtime npm and all NuGet vulnerability findings, build Compose from scratch, and run SQL Server HTTP, refresh-cookie and WebSocket smoke tests through the proxy. Test/audit/container logs are retained for 14 days. No environment file or expanded Compose configuration is uploaded. The workflow never pushes container images, changes main, or deploys infrastructure.

An Internet-facing release still needs an identified host, HTTPS certificates/termination, exact production origins, correct explicit trusted proxy configuration/rate-limit source addresses, separate least-privilege production database identities, secret management, backup/restore rehearsal, container image scanning, capacity testing, monitoring and a deployment/rollback procedure. Single-instance SignalR hints and the worker retry behavior have the limits documented in Phase 7/8 reports. Learning checkpoints remain user-owned. No live hosting deployment has been configured.

References: [Compose startup dependencies](https://docs.docker.com/compose/how-tos/startup-order/), [Microsoft SQL Server containers](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver17), [official setup-dotnet action](https://github.com/actions/setup-dotnet), [GitHub .NET CI guidance](https://docs.github.com/en/actions/tutorials/build-and-test-code/net).

## Dependency install reproducibility

Use the declared npm 11.21.1 with Node 24. CI and the Docker build stage install that CLI explicitly. npm 11.6.2 accepted an incomplete optional-peer tree that the current resolver correctly rejected; the repaired lockfile adds only `@emnapi/core` and `@emnapi/runtime`, preserving existing dependency versions. Native installation scripts are explicitly approved at their reviewed versions; unreviewed scripts fail installation through `.npmrc`. Review any new native script when updating the toolchain. See the [npm install-script policy](https://docs.npmjs.com/cli/v11/commands/npm-install-scripts/) and [clean installation behavior](https://docs.npmjs.com/cli/v11/commands/npm-ci/).
