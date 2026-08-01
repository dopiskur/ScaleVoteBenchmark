# ScaleTrigger

The solution targets **.NET 10 (LTS, supported until November 2028)**. The .NET 10 SDK is required.

The solution contains a single project, **ScaleTrigger** — a REST API that issues and validates JWT tokens, is the only component that talks to the database, and contains the models, repositories (MSSQL, MySQL, PostgreSQL and SQLite), caching, and load simulation.

The application is used purely as an API that an external load-generation script calls directly (e.g. `POST /api/vote/add?option=yes` or `?option=no`, chosen randomly per call). Each call writes a vote to the database and, along the way, generates artificial CPU, memory, disk write and network latency load — that's the purpose of the benchmark. Each load type's intensity is a `Min`/`Max` range held in the `LoadConfig` database table (see "Live-tunable load intensity" below), and a fresh random value within that range is picked for every vote; use `Min == Max` for a fixed value. Results can be read as statistics directly from the database or via `GET /api/vote/report`.

The one exception to "no UI" is a small static dashboard served at the application's root URL (`ScaleTrigger/wwwroot/index.html`), showing the live Yes/No percentage split (plain numbers, no chart), total votes, and the Payload table's row count and size (adaptively in KB/MB/GB) - or "No records found." on an empty database. It polls the anonymous `GET /api/vote/report` endpoint, which returns counts and percentages fully computed by a stored procedure/function in the database — the API only maps the returned columns onto a `VoteReport` object, it does no percentage math itself.

## Package versions

| Package | Version |
| --- | --- |
| Microsoft.Data.SqlClient | 7.0.2 |
| MySqlConnector | 2.6.1 |
| Npgsql | 10.0.3 |
| Microsoft.Data.Sqlite | 10.0.10 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.5 |
| Azure.Identity | 1.21.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 |
| Swashbuckle.AspNetCore | 7.2.0 |

Note: the version of the `Microsoft.AspNetCore.Authentication.JwtBearer` package is tied to the .NET runtime version (it's part of the ASP.NET Core shared framework), which is why the entire solution was moved to .NET 10 so that version 10.0.10 could even be used. `Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Configuration.Abstractions` are not listed separately because they already come bundled with the ASP.NET Core shared framework (`Microsoft.NET.Sdk.Web`), so there's no need for an explicit package reference. `SQLitePCLRaw.bundle_e_sqlite3` is pinned explicitly above the version `Microsoft.Data.Sqlite` pulls in transitively, since that older version has a known vulnerability (GHSA-2m69-gcr7-jv3q).

## Connecting to Azure SQL — two supported modes

`ScaleTrigger` supports two ways of connecting to Azure SQL, selected via the `UseManagedIdentity` setting in `appsettings.json`:

- **`false` (default) — connection string authentication.** Uses a classic `SqlConnection` with the username and password from `ConnectionStrings:MsSql`. Simple for local development and scenarios outside Azure, but **less secure** because the database password remains stored in the configuration file in plain text.
- **`true` — Managed Identity authentication.** Using the `Azure.Identity` package, `MsSqlRepository` obtains a temporary access token via the application's assigned Azure identity, without the password needing to exist in the configuration at all. Only works when the application is running inside an Azure environment (App Service, VM) with an assigned identity that has access to the Azure SQL database.

Both modes use the same connection string for the server address and database name; in Managed Identity mode, the username/password portion is simply ignored. The MySQL (`MySqlRepository`), PostgreSQL (`PostgreSqlRepository`) and SQLite (`SqliteRepository`) branches currently support connection string authentication only.

## JWT authentication (OptionalJwt policy)

`POST /api/vote/add`, `POST /api/loadconfig` and `POST /api/vote/reset` are all decorated with `[Authorize(Policy = "OptionalJwt")]`. Setting `Auth:Enabled` to `true` in `appsettings.json` makes them require a JWT obtained via `POST /api/auth/login`, so a load-generation script exercises token validation as part of the benchmark, not just the vote write itself. This is implemented via `OptionalAuthorizationHandler` (`ScaleTrigger/Auth/OptionalAuthorizationHandler.cs`), registered as an `IAuthorizationHandler` that succeeds every pending authorization requirement while the setting is off, so no controller code changes based on it. `GET /api/vote/report` and `GET /api/loadconfig` are always anonymous regardless of this setting; `POST /api/auth/login` keeps working either way. Defaults to `false` (frictionless anonymous load, no login needed).

## SQLite — for local development and testing, no server required

`DatabaseProvider: "Sqlite"` uses a local database file (`ConnectionStrings:Sqlite`, e.g. `Data Source=scaletrigger.db`) instead of a real Azure database — nothing to provision, nothing to reach over the network, so it's the fastest way to actually run and exercise the app. SQLite has no stored procedure/function support, so `SqliteRepository` is the one repository that runs plain parameterized SQL directly rather than calling stored routines. Everything else (the `Load:*` CPU/memory simulation, JWT auth, caching, the dashboard) behaves identically regardless of provider.

## Automatic schema provisioning at startup

Right after the database connection check succeeds, `ScaleTrigger` calls `EnsureSchemaAsync()` on the configured repository, which creates the `Vote` table (and, for MSSQL/MySQL/PostgreSQL, its stored procedures/functions) using the same connection details from `appsettings.json` — but **only if they don't already exist**. If the table is already there, `EnsureSchemaAsync()` does nothing, so an already-provisioned database and its data are never touched on subsequent restarts. This means a fresh database needs no manual setup at all: point `ConnectionStrings:<Provider>` at an empty database (or, for SQLite, just a file path) and start the app.

Creating tables/procedures requires DDL permissions on the configured database user — if that user only has DML rights (as might be the case against a shared/managed production database), `EnsureSchemaAsync()` will fail. That failure is treated the same as a failed connection check and follows `Startup:FailFastOnDbCheck` (see below): either the app refuses to start, or it logs a critical error and continues, in which case ask a DBA to provision the schema manually, using the exact DDL from `ScaleTrigger/Schema/SchemaScripts.cs`.

## Live-tunable load intensity (LoadConfig)

Six load intensities (`CpuIterationsPerVote`, `MemoryMegabytesPerVote`, `DiskWriteKilobytesPerVote`, `NetworkLatencyMillisecondsPerVote`, `PayloadBytesPerVote`, `DbCpuIterationsPerVote`) plus `ConfigRefresh` itself all live as rows in a `LoadConfig` database table, not in `appsettings.json`, so every one of them can be tuned while the app keeps running:

- **First run only:** right after schema provisioning, `LoadConfigEnsureSeededAsync()` creates the `LoadConfig` table (if missing) and, only if it's still empty, inserts one row per setting using the `Min`/`Max` values from `appsettings.json`'s `Load` section. On every later startup the table already has rows, so this is a no-op and `appsettings.json`'s `Load` section is no longer read.
- **Editing:** the dashboard (application root URL) has a "Configuration" section, split into "Refresh" (the poll interval itself - a single seconds field, since Min/Max don't apply to it), "Application" (settings simulated inside ScaleTrigger: CPU, memory, disk, network latency) and "Database" (settings simulated by the database while processing the vote: payload write, `DbCpuIterationsPerVote`) - the Application/Database rows show editable Min/Max fields, all saved together via one "Save changes" button (next to the "Reset database" button) that calls `POST /api/loadconfig`. That endpoint validates (`0 <= Min <= Max`, known setting names only) and updates the table; it only asks for a login when `Auth:Enabled` is `true` (see "JWT authentication" above), same `OptionalJwt` policy as `POST /api/vote/add`.
- **Reaching running votes:** `LoadConfigCache` is an in-memory snapshot that `VoteApiController` reads on every vote (no per-vote database round-trip). `LoadConfigRefreshService` re-reads the whole table into that cache every `ConfigRefresh` seconds - itself just another LoadConfig row, default `1` - and a successful `POST /api/loadconfig` also refreshes the cache immediately, so a save takes effect on the very next vote rather than waiting for the next poll.

## Database availability check at startup

`ScaleTrigger` immediately tries to open a connection to the configured database (without executing a query) on every startup, before it starts accepting HTTP requests. Behavior on failure is selected via the `Startup:FailFastOnDbCheck` setting:

- **`true` (default)** — the application stops immediately with a clear error in the console/log if the database is unavailable (wrong password, wrong server, closed firewall on Azure) or the schema check/bootstrap fails
- **`false`** — the error is only written as a critical log entry, and the application keeps running; useful if you want the API to remain available (e.g. for a health-check endpoint) while the database is temporarily down

Without this check, an incorrect database configuration would otherwise go unnoticed until the first real vote or result retrieval (`MsSqlRepository`/`MySqlRepository`/`PostgreSqlRepository`/`SqliteRepository` only open a connection "lazily", on an actual call, not at application startup).

## API endpoints

- `POST /api/vote/add?option=yes|no` — records a vote and generates the artificial CPU/memory/disk/network-latency load; requires a JWT if `Auth:Enabled` is `true` (see above)
- `GET /api/vote/report` — anonymous; returns `{ yes, no, total, yesPercent, noPercent }`, all computed by the database
- `POST /api/auth/login` — anonymous; body `{ "username": "...", "password": "..." }`, returns `{ "token": "..." }` on success
- `GET /api/loadconfig` — anonymous; returns the current `[{ settingName, min, max }, ...]` rows from `LoadConfig` (see "Live-tunable load intensity" above)
- `POST /api/loadconfig` — requires a JWT if `Auth:Enabled` is `true` (same as `POST /api/vote/add`); body is the same array shape, updates by `settingName`
- `POST /api/vote/reset` — requires a JWT if `Auth:Enabled` is `true` (same `OptionalJwt` policy as `POST /api/vote/add`); drops Vote, Payload and LoadConfig entirely (and their stored procedures/functions), shrinks the freed space, then recreates and reseeds everything from scratch - the "Reset database" button on the dashboard

## Deploying via GitHub Actions

The `.github/workflows/` folder contains the `deploy-api.yml` workflow, which builds and deploys `ScaleTrigger` to Azure App Service, triggered on changes within `ScaleTrigger/` or the workflow file itself.

### Setup before running the workflow for the first time

1. Create an Azure App Service resource (e.g. `scaletrigger-api`), with the .NET 10 runtime
2. In `deploy-api.yml`, replace `YOUR-API-APP-SERVICE-NAME` with the actual App Service resource name
3. Download the *Publish Profile* (Azure Portal → App Service → "Get publish profile") and store it in the GitHub repository under **Settings → Secrets and variables → Actions** as `AZURE_WEBAPP_PUBLISH_PROFILE_API`

### appsettings.json on Azure — Application Settings, not a file

Since `appsettings.json` is intentionally in `.gitignore` (it contains secrets), **it does not exist in the repository or in what gets deployed**. Instead, all settings should be configured as **Application Settings** in the Azure Portal (App Service → Configuration), where nested keys are written with a double underscore `__`:

| appsettings.json key | Application Setting name |
| --- | --- |
| `DatabaseProvider` | `DatabaseProvider` |
| `UseManagedIdentity` | `UseManagedIdentity` |
| `Auth:Enabled` | `Auth__Enabled` |
| `ConnectionStrings:MsSql` | `ConnectionStrings__MsSql` |
| `ConnectionStrings:MySql` | `ConnectionStrings__MySql` |
| `ConnectionStrings:PostgreSql` | `ConnectionStrings__PostgreSql` |
| `ConnectionStrings:Sqlite` | `ConnectionStrings__Sqlite` |
| `Jwt:Key` | `Jwt__Key` |
| `Jwt:Issuer` | `Jwt__Issuer` |
| `Jwt:Audience` | `Jwt__Audience` |
| `Jwt:ExpirationMinutes` | `Jwt__ExpirationMinutes` |
| `AdminUser:Username` | `AdminUser__Username` |
| `AdminUser:Password` | `AdminUser__Password` |
| `Load:CpuIterationsPerVote:Min` / `:Max` | `Load__CpuIterationsPerVote__Min` / `__Max` |
| `Load:MemoryMegabytesPerVote:Min` / `:Max` | `Load__MemoryMegabytesPerVote__Min` / `__Max` |
| `Load:DiskWriteKilobytesPerVote:Min` / `:Max` | `Load__DiskWriteKilobytesPerVote__Min` / `__Max` |
| `Load:NetworkLatencyMillisecondsPerVote:Min` / `:Max` | `Load__NetworkLatencyMillisecondsPerVote__Min` / `__Max` |
| `Load:PayloadBytesPerVote:Min` / `:Max` | `Load__PayloadBytesPerVote__Min` / `__Max` |
| `Load:DbCpuIterationsPerVote:Min` / `:Max` | `Load__DbCpuIterationsPerVote__Min` / `__Max` |
| `Load:ConfigRefresh:Min` / `:Max` | `Load__ConfigRefresh__Min` / `__Max` |
| `Startup:FailFastOnDbCheck` | `Startup__FailFastOnDbCheck` |
| `Cache:Enabled` | `Cache__Enabled` |
| `Cache:SlidingExpirationMinutes` | `Cache__SlidingExpirationMinutes` |

## Database setup

Not required - `EnsureSchemaAsync()` creates the schema automatically on first connection, straight from `ScaleTrigger/Schema/SchemaScripts.cs` (see "Automatic schema provisioning at startup" above), which is the only definition of the schema. Point `ConnectionStrings:<Provider>` at an empty database (or, for SQLite, just a file path) and start the app - no scripts to run by hand.

## Configuration

Before the first run, make a copy of the template:

```bash
cp ScaleTrigger/appsettings.json.example ScaleTrigger/appsettings.json
```

The real `appsettings.json` file is intentionally in `.gitignore` (it contains connection strings and secrets), while `appsettings.json.example` stays under version control as a template.

In `ScaleTrigger/appsettings.json` you need to set:

- `DatabaseProvider` — `"MsSql"`, `"MySql"`, `"PostgreSql"` or `"Sqlite"`, selects which repository implementation is used
- `ConnectionStrings:MsSql`, `ConnectionStrings:MySql`, `ConnectionStrings:PostgreSql` and `ConnectionStrings:Sqlite` — connection strings/paths for all four (none of the others need to be valid, only the one matching the selected provider is used)
- `Load:CpuIterationsPerVote`, `Load:MemoryMegabytesPerVote`, `Load:DiskWriteKilobytesPerVote`, `Load:NetworkLatencyMillisecondsPerVote`, `Load:PayloadBytesPerVote` and `Load:DbCpuIterationsPerVote` — each is a `{ "Min": ..., "Max": ... }` object, used only to seed the `LoadConfig` database table the very first time it's created (see "Live-tunable load intensity" above); edit the running values from the dashboard afterwards, not here. A fresh random value in the inclusive range is picked for every vote (`Min == Max` gives a fixed value). `CpuIterationsPerVote` and `DbCpuIterationsPerVote` both run sysbench's CPU benchmark algorithm - counting primes up to that value by trial division of each candidate by every integer up to its square root (`LoadSimulator.SimulateCpuLoad` in the app; `VoteAdd`'s `@MaxPrime`/`pMaxPrime`/`p_max_prime` parameter in the database) - cost grows roughly as N^1.5, not linearly, so keep these values much lower than you would a plain iteration count. The disk write goes to a uniquely-named temp file per call (`%TEMP%/ScaleTrigger/diskload` on Windows, the OS temp dir elsewhere), forced to disk with `FileOptions.WriteThrough` + `Flush(true)`, then deleted immediately - so concurrent votes don't contend on a shared file and nothing accumulates on disk over a long run. The network latency delay uses `Task.Delay` (non-blocking `await`), not a thread-blocking sleep, so it doesn't tie up a request thread while "waiting" - the same way a real async call to a downstream service would behave, and without artificially capping how much concurrent load the app can take. `DbCpuIterationsPerVote` runs inside the database engine itself, not in the application
- `Load:ConfigRefresh` — also a `{ "Min": ..., "Max": ... }` object, seeds the poll interval (seconds) between checks of the `LoadConfig` table for dashboard edits (default `{ "Min": 1, "Max": 1 }`); only affects how fast an edit takes effect, not the values themselves
- `Cache:Enabled` — `true` (default) enables the MemoryCache for the voting report; `false` disables caching so every `GET /api/vote/report` goes straight to the database (useful when benchmarking database load alone, without cache influence)
- `Auth:Enabled` — `false` (default) leaves `POST /api/vote/add` reachable without a token; `true` requires a JWT there
- `Jwt:Key` — signing key, at least 32 characters; defaults to a plain sequential placeholder (`abcdefghijklmnopqrstuvwxyz012345`) since this is a stress-test tool with no real secrets to protect - replace it if that ever stops being true
- `AdminUser:Username` / `AdminUser:Password` — credentials for administrator login (default `admin` / `admin`)

## Running

```bash
cd ScaleTrigger
dotnet run
```

Votes are generated by calling `POST /api/vote/add?option=yes` or `POST /api/vote/add?option=no` — a load-testing script that randomly picks `yes`/`no` per call needs a JWT first (`POST /api/auth/login` with `AdminUser:Username`/`AdminUser:Password`) only if `Auth:Enabled` is `true`. Results can be read directly from the `Vote` table in the database. The live percentage dashboard is at the application's root URL and needs no login, backed by the anonymous `GET /api/vote/report` endpoint.

## Generating load (scripts/scaleTriggerLoad.py)

`scripts/scaleTriggerLoad.py` is an external Python load generator that calls a real, already-running instance of the API (not in-process) at a target votes-per-second rate, optionally spread across multiple processes so a single process's GIL/network overhead doesn't become the bottleneck. Requires `aiohttp` (the script offers to `pip install` it automatically if missing). It probes the API with one unauthenticated request first and, only if that comes back `401` (i.e. `Auth:Enabled` is `true`), logs in with `admin`/`admin` and attaches the resulting JWT to every subsequent request - no flag needed either way.

```bash
# 20 votes/s for 60s - authentication is detected automatically
python scripts/scaleTriggerLoad.py --url https://scaletrigger-api.azurewebsites.net --votes 20 --duration 60

# 200 votes/s, spread across 4 parallel processes
python scripts/scaleTriggerLoad.py --url https://scaletrigger-api.azurewebsites.net --votes 200 --duration 120 --process 4

# Ramp-up: start at 10 votes/s, +10% every 5s, for 5 minutes - useful for finding
# the breaking point of an instance before autoscale kicks in
python scripts/scaleTriggerLoad.py --url https://scaletrigger-api.azurewebsites.net --votes 10 --duration 300 --ramp true --ramp-step 10 --ramp-interval 5
```

Run with no arguments for a quick parameter summary, or `--help` for full descriptions.

## Vote table primary key

The `Vote` table's primary key (`IDVote` / `id_vote`) is `BIGINT`/`BIGSERIAL` in the MSSQL/MySQL/PostgreSQL schemas, not a 32-bit `INT`, since this is a load-testing tool expected to accumulate a very large number of rows. The SQLite schema uses `INTEGER PRIMARY KEY AUTOINCREMENT`, which is already a 64-bit rowid in SQLite (there's no separate `INT`/`BIGINT` distinction), so no separate sizing decision is needed there.
