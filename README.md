# ScaleVoteBenchmark

The solution targets **.NET 10 (LTS, supported until November 2028)**. The .NET 10 SDK is required.

The solution contains a single project, **ScaleVoteBenchmark.Api** — a REST API that issues and validates JWT tokens, is the only component that talks to the database, and contains the models, repositories (MSSQL, MySQL, PostgreSQL and SQLite), caching, and load simulation.

The application is used purely as an API that an external load-generation script calls directly (e.g. `POST /api/vote/add?option=yes` or `?option=no`, chosen randomly per call). Each call writes a vote to the database and, along the way, generates artificial CPU/memory load whose intensity is defined in `appsettings.json` (`Load:CpuIterationsPerVote`, `Load:MemoryMegabytesPerVote`) — that's the purpose of the benchmark. Results (`GET /api/vote/counts`) can later be read as statistics directly from the database or via that endpoint; whether it requires a JWT is controlled by `Auth:Enabled` (see below).

The one exception to "no UI" is a small static dashboard served at the application's root URL (`ScaleVoteBenchmark.Api/wwwroot/index.html`), showing the live Yes/No percentage split. It polls the anonymous `GET /api/vote/report` endpoint, which returns counts and percentages fully computed by a stored procedure/function in the database (the same pattern as `VoteCountsGet`, just with percentage columns added) — the API only maps the returned columns onto a `VoteReport` object, it does no percentage math itself.

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

`ScaleVoteBenchmark.Api` supports two ways of connecting to Azure SQL, selected via the `UseManagedIdentity` setting in `appsettings.json`:

- **`false` (default) — connection string authentication.** Uses a classic `SqlConnection` with the username and password from `ConnectionStrings:MsSql`. Simple for local development and scenarios outside Azure, but **less secure** because the database password remains stored in the configuration file in plain text.
- **`true` — Managed Identity authentication.** Using the `Azure.Identity` package, `MsSqlRepository` obtains a temporary access token via the application's assigned Azure identity, without the password needing to exist in the configuration at all. Only works when the application is running inside an Azure environment (App Service, VM) with an assigned identity that has access to the Azure SQL database.

Both modes use the same connection string for the server address and database name; in Managed Identity mode, the username/password portion is simply ignored. The MySQL (`MySqlRepository`), PostgreSQL (`PostgreSqlRepository`) and SQLite (`SqliteRepository`) branches currently support connection string authentication only.

## Making JWT authentication optional

`GET /api/vote/counts` is decorated with `[Authorize]` and normally requires a JWT obtained via `POST /api/auth/login`. Setting `Auth:Enabled` to `false` in `appsettings.json` makes it reachable without a token — useful when you just want to poll counts locally without dealing with login. This is implemented via `OptionalAuthorizationHandler` (`ScaleVoteBenchmark.Api/Auth/OptionalAuthorizationHandler.cs`), registered as an `IAuthorizationHandler` that succeeds every pending authorization requirement when the setting is off, so no controller code changes based on it. `POST /api/vote/add` and `GET /api/vote/report` are already anonymous regardless of this setting; `POST /api/auth/login` keeps working either way (in case you flip `Auth:Enabled` back on later). Defaults to `true`.

## SQLite — for local development and testing, no server required

`DatabaseProvider: "Sqlite"` uses a local database file (`ConnectionStrings:Sqlite`, e.g. `Data Source=scalevotebenchmark.db`) instead of a real Azure database — nothing to provision, nothing to reach over the network, so it's the fastest way to actually run and exercise the app. SQLite has no stored procedure/function support, so `SqliteRepository` is the one repository that runs plain parameterized SQL directly rather than calling stored routines. Everything else (the `Load:*` CPU/memory simulation, JWT auth, caching, the dashboard) behaves identically regardless of provider.

## Automatic schema provisioning at startup

Right after the database connection check succeeds, `ScaleVoteBenchmark.Api` calls `EnsureSchema()` on the configured repository, which creates the `Vote` table (and, for MSSQL/MySQL/PostgreSQL, its stored procedures/functions) using the same connection details from `appsettings.json` — but **only if they don't already exist**. If the table is already there, `EnsureSchema()` does nothing, so an already-provisioned database and its data are never touched on subsequent restarts. This means a fresh database needs no manual setup at all: point `ConnectionStrings:<Provider>` at an empty database (or, for SQLite, just a file path) and start the app.

The `sql/*.sql` scripts remain available for manual provisioning or an explicit drop-and-recreate reset (see "Database setup" below); they are not used by the app itself.

Creating tables/procedures requires DDL permissions on the configured database user — if that user only has DML rights (as might be the case against a shared/managed production database), `EnsureSchema()` will fail. That failure is treated the same as a failed connection check and follows `Startup:FailFastOnDbCheck` (see below): either the app refuses to start, or it logs a critical error and continues, in which case run the appropriate `sql/*.sql` script manually instead.

## Database availability check at startup

`ScaleVoteBenchmark.Api` immediately tries to open a connection to the configured database (without executing a query) on every startup, before it starts accepting HTTP requests. Behavior on failure is selected via the `Startup:FailFastOnDbCheck` setting:

- **`true` (default)** — the application stops immediately with a clear error in the console/log if the database is unavailable (wrong password, wrong server, closed firewall on Azure) or the schema check/bootstrap fails
- **`false`** — the error is only written as a critical log entry, and the application keeps running; useful if you want the API to remain available (e.g. for a health-check endpoint) while the database is temporarily down

Without this check, an incorrect database configuration would otherwise go unnoticed until the first real vote or result retrieval (`MsSqlRepository`/`MySqlRepository`/`PostgreSqlRepository`/`SqliteRepository` only open a connection "lazily", on an actual call, not at application startup).

## Deploying via GitHub Actions

The `.github/workflows/` folder contains the `deploy-api.yml` workflow, which builds and deploys `ScaleVoteBenchmark.Api` to Azure App Service, triggered on changes within `ScaleVoteBenchmark.Api/` or the workflow file itself.

### Setup before running the workflow for the first time

1. Create an Azure App Service resource (e.g. `scalevotebenchmark-api`), with the .NET 10 runtime
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
| `Load:CpuIterationsPerVote` | `Load__CpuIterationsPerVote` |
| `Load:MemoryMegabytesPerVote` | `Load__MemoryMegabytesPerVote` |
| `Startup:FailFastOnDbCheck` | `Startup__FailFastOnDbCheck` |
| `Cache:Enabled` | `Cache__Enabled` |
| `Cache:SlidingExpirationMinutes` | `Cache__SlidingExpirationMinutes` |

## Database setup

Not required for a fresh database - `EnsureSchema()` creates the schema automatically at startup (see above). The `sql/` scripts are there for manual provisioning or an explicit drop-and-recreate reset:

- `sql/mssql_schema.sql` for Azure SQL
- `sql/mysql_schema.sql` for Azure Database for MySQL
- `sql/postgresql_schema.sql` for Azure Database for PostgreSQL
- `sql/sqlite_schema.sql` for a local SQLite file (table only - no stored routines, see above)

## Configuration

Before the first run, make a copy of the template:

```bash
cp ScaleVoteBenchmark.Api/appsettings.json.example ScaleVoteBenchmark.Api/appsettings.json
```

The real `appsettings.json` file is intentionally in `.gitignore` (it contains connection strings and secrets), while `appsettings.json.example` stays under version control as a template.

In `ScaleVoteBenchmark.Api/appsettings.json` you need to set:

- `DatabaseProvider` — `"MsSql"`, `"MySql"`, `"PostgreSql"` or `"Sqlite"`, selects which repository implementation is used
- `ConnectionStrings:MsSql`, `ConnectionStrings:MySql`, `ConnectionStrings:PostgreSql` and `ConnectionStrings:Sqlite` — connection strings/paths for all four (none of the others need to be valid, only the one matching the selected provider is used)
- `Load:CpuIterationsPerVote` and `Load:MemoryMegabytesPerVote` — intensity of the artificial CPU and memory load per vote
- `Cache:Enabled` — `true` (default) enables the MemoryCache for voting results; `false` disables caching so every `GET /api/vote/counts` goes straight to the database (useful when benchmarking database load alone, without cache influence)
- `Auth:Enabled` — `true` (default) requires a JWT on `GET /api/vote/counts`; `false` makes it reachable without one
- `Jwt:Key` — signing key, at least 32 characters; defaults to a plain sequential placeholder (`abcdefghijklmnopqrstuvwxyz012345`) since this is a stress-test tool with no real secrets to protect - replace it if that ever stops being true
- `AdminUser:Username` / `AdminUser:Password` — credentials for administrator login (default `admin` / `admin`)

## Running

```bash
cd ScaleVoteBenchmark.Api
dotnet run
```

Votes are generated by calling `POST /api/vote/add?option=yes` or `POST /api/vote/add?option=no` directly (anonymous access, no JWT token needed) — e.g. by a load-testing script that randomly picks `yes`/`no` per call. Summed results are available at `GET /api/vote/counts` (requires a JWT obtained via `POST /api/auth/login`), and can also be read directly from the `Vote` table in the database. The live percentage dashboard is at the application's root URL and needs no login, backed by the anonymous `GET /api/vote/report` endpoint.

## Vote table primary key

The `Vote` table's primary key (`IDVote` / `id_vote`) is `BIGINT`/`BIGSERIAL` in the MSSQL/MySQL/PostgreSQL schemas, not a 32-bit `INT`, since this is a load-testing tool expected to accumulate a very large number of rows. The SQLite schema uses `INTEGER PRIMARY KEY AUTOINCREMENT`, which is already a 64-bit rowid in SQLite (there's no separate `INT`/`BIGINT` distinction), so no separate sizing decision is needed there.
