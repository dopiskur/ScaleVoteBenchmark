# ScaleVoteBenchmark

The solution targets **.NET 10 (LTS, supported until November 2028)**. The .NET 10 SDK is required.

The solution contains a single project, **ScaleVoteBenchmark.Api** — a REST API that issues and validates JWT tokens, is the only component that talks to the database, and contains the models, repositories (MSSQL, MySQL and PostgreSQL), caching, and load simulation.

The application has no user interface — it is intentionally used purely as an API that an external load-generation script calls directly (e.g. `POST /api/vote/add?option=yes` or `?option=no`, chosen randomly per call). Each call writes a vote to the database and, along the way, generates artificial CPU/memory load whose intensity is defined in `appsettings.json` (`Load:CpuIterationsPerVote`, `Load:MemoryMegabytesPerVote`) — that's the purpose of the benchmark. Results (`GET /api/vote/counts`, protected by JWT) can later be read as statistics directly from the database or via that endpoint.

## Package versions

| Package | Version |
| --- | --- |
| Microsoft.Data.SqlClient | 7.0.2 |
| MySqlConnector | 2.6.1 |
| Npgsql | 10.0.3 |
| Azure.Identity | 1.21.0 |
| Microsoft.AspNetCore.Authentication.JwtBearer | 10.0.10 |
| Swashbuckle.AspNetCore | 7.2.0 |

Note: the version of the `Microsoft.AspNetCore.Authentication.JwtBearer` package is tied to the .NET runtime version (it's part of the ASP.NET Core shared framework), which is why the entire solution was moved to .NET 10 so that version 10.0.10 could even be used. `Microsoft.Extensions.Caching.Memory` and `Microsoft.Extensions.Configuration.Abstractions` are not listed separately because they already come bundled with the ASP.NET Core shared framework (`Microsoft.NET.Sdk.Web`), so there's no need for an explicit package reference.

## Connecting to Azure SQL — two supported modes

`ScaleVoteBenchmark.Api` supports two ways of connecting to Azure SQL, selected via the `UseManagedIdentity` setting in `appsettings.json`:

- **`false` (default) — connection string authentication.** Uses a classic `SqlConnection` with the username and password from `ConnectionStrings:MsSql`. Simple for local development and scenarios outside Azure, but **less secure** because the database password remains stored in the configuration file in plain text.
- **`true` — Managed Identity authentication.** Using the `Azure.Identity` package, `MsSqlRepository` obtains a temporary access token via the application's assigned Azure identity, without the password needing to exist in the configuration at all. Only works when the application is running inside an Azure environment (App Service, VM) with an assigned identity that has access to the Azure SQL database.

Both modes use the same connection string for the server address and database name; in Managed Identity mode, the username/password portion is simply ignored. The MySQL (`MySqlRepository`) and PostgreSQL (`PostgreSqlRepository`) branches currently support connection string authentication only.

## Database availability check at startup

`ScaleVoteBenchmark.Api` immediately tries to open a connection to the configured database (without executing a query) on every startup, before it starts accepting HTTP requests. Behavior on failure is selected via the `Startup:FailFastOnDbCheck` setting:

- **`true` (default)** — the application stops immediately with a clear error in the console/log if the database is unavailable (wrong password, wrong server, closed firewall on Azure)
- **`false`** — the error is only written as a critical log entry, and the application keeps running; useful if you want the API to remain available (e.g. for a health-check endpoint) while the database is temporarily down

Without this check, an incorrect database configuration would otherwise go unnoticed until the first real vote or result retrieval (`MsSqlRepository`/`MySqlRepository`/`PostgreSqlRepository` only open a connection "lazily", on an actual call, not at application startup).

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
| `ConnectionStrings:MsSql` | `ConnectionStrings__MsSql` |
| `ConnectionStrings:MySql` | `ConnectionStrings__MySql` |
| `ConnectionStrings:PostgreSql` | `ConnectionStrings__PostgreSql` |
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

Run the appropriate script from the `sql/` directory against the chosen database:

- `sql/mssql_schema.sql` for Azure SQL
- `sql/mysql_schema.sql` for Azure Database for MySQL
- `sql/postgresql_schema.sql` for Azure Database for PostgreSQL

## Configuration

Before the first run, make a copy of the template:

```bash
cp ScaleVoteBenchmark.Api/appsettings.json.example ScaleVoteBenchmark.Api/appsettings.json
```

The real `appsettings.json` file is intentionally in `.gitignore` (it contains connection strings and secrets), while `appsettings.json.example` stays under version control as a template.

In `ScaleVoteBenchmark.Api/appsettings.json` you need to set:

- `DatabaseProvider` — `"MsSql"`, `"MySql"` or `"PostgreSql"`, selects which repository implementation is used
- `ConnectionStrings:MsSql`, `ConnectionStrings:MySql` and `ConnectionStrings:PostgreSql` — connection strings for all three databases (none of the others need to be valid, only the one matching the selected provider is used)
- `Load:CpuIterationsPerVote` and `Load:MemoryMegabytesPerVote` — intensity of the artificial CPU and memory load per vote
- `Cache:Enabled` — `true` (default) enables the MemoryCache for voting results; `false` disables caching so every `GET /api/vote/counts` goes straight to the database (useful when benchmarking database load alone, without cache influence)
- `Jwt:Key` — a random secret key, at least 32 characters
- `AdminUser:Username` / `AdminUser:Password` — credentials for administrator login

## Running

```bash
cd ScaleVoteBenchmark.Api
dotnet run
```

Votes are generated by calling `POST /api/vote/add?option=yes` or `POST /api/vote/add?option=no` directly (anonymous access, no JWT token needed) — e.g. by a load-testing script that randomly picks `yes`/`no` per call. Summed results are available at `GET /api/vote/counts` (requires a JWT obtained via `POST /api/auth/login`), and can also be read directly from the `Vote` table in the database.
