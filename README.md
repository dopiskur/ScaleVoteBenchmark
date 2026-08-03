# ScaleTrigger

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A REST API for testing autoscale triggers on the app tier (Azure App Service, Container Apps, AKS, or anywhere else) and on the database tier (e.g. Azure SQL serverless, Flexible Server autoscale). How much CPU/memory/disk/network each request burns, in the app and separately inside the database itself, can be turned up or down **live**, from a dashboard or a single API call, while traffic keeps flowing. No redeploy, no restart, and no need to stop the run and repeat the whole experiment just to try a different intensity; scale-up and scale-down thresholds can both be swept in the same run.

ScaleTrigger is not a load generator itself; you point a real load-testing tool at it (see "Generating load" below) while adjusting how expensive each request is on the fly.

The solution targets **.NET 10 (LTS, supported until November 2028)**. The .NET 10 SDK is required.

## How it works

Every `POST /api/vote/add?option=yes|no` call:

1. Records a vote in the database.
2. Burns a controllable, randomized amount of CPU, memory, disk I/O, and network latency (the actual point of the benchmark).

### Live-tunable load

Every `POST /api/vote/add` call picks a fresh random value, per load type, from a `Min`/`Max` range. Unlike a value baked into `appsettings.json`, these ranges live in the `LoadConfig` database table and can be changed while the app is running and under test, with no restart or redeploy:

- `CpuIterationsPerVote`: chained SHA-512 hashing in the app process (each hash's output feeds the next, so the JIT can't fold the loop away)
- `MemoryKilobytesPerVote`: a buffer allocated and touched page-by-page so the OS actually reserves physical memory, not just address space
- `DiskWriteKilobytesPerVote`: a uniquely-named temp file per call, written with `FileOptions.WriteThrough` + `Flush(true)` (real disk I/O, not page cache) then deleted immediately
- `NetworkLatencyMillisecondsPerVote`: a non-blocking `Task.Delay`, so it frees the request thread the way a real downstream call would
- `PayloadBytesPerVote`: optional extra random bytes written into a `Payload` table row (off by default, since unlike the others this permanently grows the database; opt in for write-throughput benchmarking)
- `DbCpuIterationsPerVote`: CPU burn that runs inside the database engine itself (a `DbCpuBurn` stored procedure/function), not the app; add `&dbCpuBurnOnly=true` to `POST /api/vote/add` to isolate it from the `Vote`/`Payload` insert entirely
- `ConfigRefresh`: not a vote-time load setting; it's how often (in seconds) the app re-polls `LoadConfig` for changes made via the dashboard/API, so it controls how fast an edit to any of the above takes effect

Results can be read directly from the `Vote` table or via `GET /api/vote/report`, which returns the total vote count and payload stats, fully computed by the database (a stored procedure/function, or plain SQL on SQLite, see "Database providers").

## Why not just use Chaos Studio / AWS FIS / Chaos Mesh / stress-ng?

None of them offer a live dial you can turn while traffic keeps flowing and watch the very next request pick up the new value. That's specifically what ScaleTrigger's `LoadConfig` table + dashboard does. It also means the load is exercised as real HTTP traffic through your actual routing/ingress/load balancer path, not injected directly at the VM/pod level, which matters if what you're validating is a scaler that reacts to request rate or concurrency rather than raw CPU%, since Chaos Studio's agent-based faults don't exercise that on their own.

Those are legitimate, more mature tools for the general "put CPU/memory pressure on something" problem, but:
- Chaos Studio / AWS FIS: change the level → define or launch a new experiment (or a pre-scripted sequence of steps, decided in advance).
- Chaos Mesh: change the level → edit the CRD and `kubectl apply` it again.
- stress-ng: change the level → kill the process and restart it with new flags.
- k6 / Locust / Azure Load Testing: don't control per-request resource cost at all; they control requests per second, and what each request costs server-side is entirely up to the target app.

None of this makes ScaleTrigger a replacement for those tools. For infrastructure-level fault injection (killing instances, network faults, service failures) it does nothing at all, and for a one-off "just burn 80% CPU for five minutes" test, stress-ng is simpler and needs zero custom code. Use ScaleTrigger specifically when you want to sweep or fine-tune a request-driven load profile live, without interrupting the traffic that's already hitting the trigger you're testing.

## Quickstart

**Azure**: one click, provisions and deploys everything.

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fdopiskur%2FscaleTrigger%2Fmaster%2Fdeploy%2Fazure%2Fmain.json)

For repeatable deploys afterward or a manual Bicep run instead of the button, see [deploy/azure/README.md](deploy/azure/README.md).

**Docker**, no Azure account or manual setup needed:

```bash
docker compose up --build
```

This starts a local PostgreSQL container and the app together (see `docker-compose.yml`), with the schema created automatically on first start. Open `http://localhost:8080` for the live dashboard.

For a lighter local setup with no containers at all:

```bash
cd ScaleTrigger
cp appsettings.json.example appsettings.json
dotnet run
```

With the example config's default `DatabaseProvider: "Sqlite"`, this needs nothing else, just a local file (`scaletrigger.db`), created automatically on first run.

## Configuration

`appsettings.json` is gitignored (it holds connection strings and secrets); `appsettings.json.example` is the checked-in template. Copy it before running:

```bash
cp ScaleTrigger/appsettings.json.example ScaleTrigger/appsettings.json
```

Key settings:

| Setting | Purpose |
|---|---|
| `DatabaseProvider` | `"MsSql"`, `"MySql"`, `"PostgreSql"`, or `"Sqlite"`; selects the repository implementation |
| `ConnectionStrings:<Provider>` | Connection string for the selected provider (the others don't need to be valid) |
| `UseManagedIdentity` | MSSQL only. `true` authenticates via Azure Managed Identity instead of a plain-text password (see below) |
| `Auth:Enabled` | `false` (default): `POST /api/vote/add` and other admin actions need no token. `true`: they require a JWT from `POST /api/auth/login` |
| `Startup:FailFastOnDbCheck` | `false` (default): log a critical error and keep running if the database is unreachable. `true`: refuse to start |
| `Cache:Enabled` / `Cache:SlidingExpirationMinutes` | Caches `GET /api/vote/report` in memory; disable to benchmark database read load in isolation |
| `Load:*` | Seeds the initial `LoadConfig` values (see "How it works" above); after the first run, edit these live instead |
| `Load:ConfigRefresh` | How often (seconds) a live edit to `Load:*` takes effect |
| `NodeBenchmark:*` | Duration/size/repetitions for the on-demand CPU/memory/disk saturation test (`POST /api/nodebenchmark/run`), unrelated to per-vote load |
| `Jwt:Key` / `Jwt:Issuer` / `Jwt:Audience` / `Jwt:ExpirationMinutes` | JWT signing config. The default key is a placeholder; replace it if `Auth:Enabled` is ever `true` outside a throwaway environment |
| `AdminUser:Username` / `AdminUser:Password` | Login credentials for `POST /api/auth/login` (default `admin`/`admin`; this is a stress-test tool, not a production auth system, so don't reuse real credentials here) |

### Database providers

Four repository implementations exist, selected via `DatabaseProvider`: MSSQL, MySQL, PostgreSQL, and SQLite. SQLite is the odd one out: it has no stored procedure/function support, so its repository runs plain parameterized SQL directly instead of calling stored routines. Everything else (load simulation, auth, caching, the dashboard, dynamic `LoadConfig`) behaves identically regardless of provider.

### Connecting to Azure SQL

Two authentication modes, selected via `UseManagedIdentity`:

- **`false` (default)**: plain connection string (username/password from `ConnectionStrings:MsSql`). Simple, but the password sits in the config file in plain text.
- **`true`**: Managed Identity via the `Azure.Identity` package; the app obtains a temporary access token instead. Only works inside an Azure environment (App Service, VM) with an assigned identity that has database access.

MySQL, PostgreSQL, and SQLite currently support connection string authentication only.

### JWT authentication

`Auth:Enabled = true` makes `POST /api/vote/add`, `POST /api/vote/reset`, `POST /api/loadconfig`, and `POST /api/nodebenchmark/run` require a JWT from `POST /api/auth/login`, so a load test also exercises token validation as part of the benchmark, not just the vote write. This is implemented via `OptionalAuthorizationHandler`, which succeeds every pending authorization requirement while the setting is off, so no controller code changes based on it. `GET /api/vote/report`, `GET /api/loadconfig`, and `GET /api/nodebenchmark/hardware` are always anonymous.

## Schema provisioning

There is no separate SQL file to run manually. `EnsureSchemaAsync()` creates the `Vote` table (and, for MSSQL/MySQL/PostgreSQL, its stored procedures/functions) from schema embedded directly in the code (`ScaleTrigger/Schema/SchemaScripts.cs`), but only if the table doesn't already exist, so an already-provisioned database is never touched on restart. Point `ConnectionStrings:<Provider>` at an empty database (or, for SQLite, just a file path) and start the app; nothing else is required.

Creating tables/procedures requires DDL permissions on the configured database user. If the user only has DML rights, `EnsureSchemaAsync()` fails, and that failure follows `Startup:FailFastOnDbCheck` the same way a connection failure would.

PostgreSQL additionally runs `CREATE EXTENSION IF NOT EXISTS pgcrypto` as part of provisioning (the database-side CPU burn's SHA-512 hashing uses pgcrypto's `digest()`), which needs a role with the privilege to create extensions. That's usually superuser, though most managed offerings (e.g. Azure Database for PostgreSQL) grant it via a dedicated admin role instead.

## API endpoints

| Endpoint | Auth | Purpose |
|---|---|---|
| `POST /api/vote/add?option=yes\|no` | optional (`Auth:Enabled`) | Records a vote and generates the per-vote CPU/memory/disk/network load. Add `&dbCpuBurnOnly=true` to isolate the database-side CPU burn (`DbCpuBurn`/`db_cpu_burn`) from the `Vote`/`Payload` insert; no row is written |
| `GET /api/vote/report` | anonymous | `{ total, payloadCount, payloadTotalBytes }`, computed by the database |
| `POST /api/vote/reset` | optional | Drops and recreates the schema; database ends up looking brand new |
| `POST /api/auth/login` | anonymous | Body `{ "username", "password" }` → `{ "token" }` |
| `GET /api/loadconfig` | anonymous | Current `Load:*` ranges as stored in the database |
| `POST /api/loadconfig` | optional | Updates one or more `Load:*` ranges live, see "How it works" |
| `GET /api/nodebenchmark/hardware` | anonymous | Detects the hosting environment (Azure App Service/Container Apps, AWS ECS/EC2, Kubernetes, generic Docker, bare metal) and reports CPU/memory/disk |
| `POST /api/nodebenchmark/run` | optional | Runs a one-off CPU/memory/disk saturation benchmark on the current node (~20s by default) |

## Dashboard

A small static dashboard is served at the application's root URL (`ScaleTrigger/wwwroot/index.html`); no separate frontend project. It shows the live total vote count and payload stats (polling `GET /api/vote/report`), lets you turn the `LoadConfig` ranges up or down while traffic is running, and can trigger the node hardware benchmark. It needs no login regardless of `Auth:Enabled`.

**CPU calibration:** "Run benchmark" measures the node's raw hash throughput, then briefly drops `CpuIterationsPerVote`/`DbCpuIterationsPerVote` to near-zero and times a few real votes to isolate everything else a vote costs (network, DB round trip). From those two numbers and a target concurrency `N` you enter, it computes a `CpuIterationsPerVote` range that should saturate all cores at exactly `N` concurrent votes; "Set recommended" writes it into the Min/Max fields above (still needs "Save changes" to persist). Original load settings are restored automatically once the calibration votes finish.

## Generating load

ScaleTrigger doesn't generate its own traffic; point one of these at it. All three live in `scripts/` and share the same behavior: `POST /api/vote/add` with `yes`/`no` chosen randomly per call, and automatic JWT login if the API responds `401` (detected via a probe request, using `AdminUser:Username`/`AdminUser:Password`).

| Script | When to use it |
|---|---|
| `scaleTriggerLoad_k6.js` | Default choice for most runs: precise ramp-up (`stages`), CI-friendly pass/fail thresholds, and the most mature reporting of the three. |
| `scaleTriggerLoad_locust.py` | Same test, runnable locally (`locust -f ...`) **or uploaded directly to Azure Load Testing as a Locust test**, unmodified. Note: Locust ramps by number of simulated users, not by a direct requests-per-second target, so expect the actual rate to be an approximation, not an exact number. |
| `scaleTriggerLoad.py` | Legacy option, kept for one specific reason: it targets an **exact votes-per-second rate** regardless of API latency (via a scheduled async loop), which neither k6's arrival-rate executor nor Locust's user-based model guarantee as precisely. Use this if you need to say "the trigger fired at exactly N votes/s" rather than an approximate rate. |

See the header comment in each script for full usage examples and parameters (`--url`/`URL`, `--votes`/`VOTES`, `--ramp`/`RAMP`, etc.; parameter names differ slightly per tool but map to the same concepts).

While a load-test script drives the request rate, the `LoadConfig` dashboard/API drives what each of those requests costs server-side. The two are independent knobs, and both can be turned during the same run.

## Deploying via GitHub Actions

Need to actually stand up an App Service resource first, or want a one-click deploy? See [deploy/azure/README.md](deploy/azure/README.md).

`.github/workflows/deploy-api.yml` builds and deploys `ScaleTrigger` to Azure App Service, triggered on changes within `ScaleTrigger/` or the workflow file itself.

Before running it the first time:

1. Create an Azure App Service resource (e.g. `scaletrigger-api`) with the .NET 10 runtime.
2. In `deploy-api.yml`, replace `YOUR-API-APP-SERVICE-NAME` with the actual App Service resource name.
3. Download the *Publish Profile* (Azure Portal → App Service → "Get publish profile") and store it as the `AZURE_WEBAPP_PUBLISH_PROFILE_API` secret under **Settings → Secrets and variables → Actions**.

### appsettings.json on Azure: Application Settings, not a file

Since `appsettings.json` never ships in the deployed package, every setting must be set as an **Application Setting** in the Azure Portal (App Service → Configuration), with nested keys joined by a double underscore. For example, `ConnectionStrings:MsSql` becomes `ConnectionStrings__MsSql`, `Auth:Enabled` becomes `Auth__Enabled`, `Load:CpuIterationsPerVote:Min` becomes `Load__CpuIterationsPerVote__Min`, and so on for every nested key in `appsettings.json.example`.

## Package versions

| Package | Version |
|---|---|
| Microsoft.Data.SqlClient | 7.0.2 |
| MySqlConnector | 2.6.1 |
| Npgsql | 10.0.3 |
| Microsoft.Data.Sqlite | 10.0.10 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 (pinned above the version `Microsoft.Data.Sqlite` pulls in transitively; that older version has a known vulnerability, GHSA-2m69-gcr7-jv3q) |
| Azure.Identity | 1.21.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 (tied to the .NET runtime version) |

`Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Configuration.Abstractions` need no explicit reference; both come bundled with the ASP.NET Core shared framework (`Microsoft.NET.Sdk.Web`).

## Notes on the `Vote` table primary key

`IDVote`/`id_vote` is `BIGINT`/`BIGSERIAL` on MSSQL/MySQL/PostgreSQL, not a 32-bit `INT`, since this tool is expected to accumulate a very large row count under sustained load testing. SQLite's `INTEGER PRIMARY KEY AUTOINCREMENT` is already a 64-bit rowid, so no separate sizing decision was needed there.
