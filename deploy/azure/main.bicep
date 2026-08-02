// ScaleTrigger - Azure App Service deployment.
//
// Provisions a Linux App Service Plan + App Service, wires up GitHub source
// control so App Service's own Oryx build picks up ScaleTrigger.sln straight
// from the repo (no separate CI/CD needed for a first deploy - see
// deploy/azure/README.md, scenario 1), and sets every Application Setting
// that appsettings.json.example seeds ScaleTrigger from, using the
// ':' -> '__' nesting convention documented in the main README.
//
// This is INFRASTRUCTURE + CONFIGURATION only. For repeatable deploys after
// the first one, use the GitHub Actions workflow instead (scenario 2) -
// this template does not need to be re-run for every code change.
//
// Compiled to main.json via `az bicep build` - see deploy/azure/README.md.

@description('Name of the App Service (must be globally unique - becomes <name>.azurewebsites.net).')
@minLength(2)
@maxLength(60)
param appServiceName string

@description('Azure region for all resources.')
param location string = resourceGroup().location

@description('App Service Plan SKU. Autoscale rules (the whole point of this tool) need Standard tier or higher - Basic/Free plans cannot be autoscaled.')
@allowed([
  'S1'
  'S2'
  'S3'
  'P0v3'
  'P1v3'
  'P2v3'
  'P3v3'
])
param skuName string = 'S1'

@description('Public GitHub repository to build and deploy from (Oryx builds ScaleTrigger.sln automatically - no publish profile or GitHub token needed for a public repo).')
param repositoryUrl string = 'https://github.com/dopiskur/scaleTrigger'

@description('Branch to deploy from.')
param branch string = 'master'

// --- DatabaseProvider ---------------------------------------------------

@description('Selects the repository implementation - matches appsettings.json.example. Sqlite needs no external database but its file lives on ephemeral App Service storage (wiped on restart/scale) - fine to try the deploy, not for anything you need to keep.')
@allowed([
  'MsSql'
  'MySql'
  'PostgreSql'
  'Sqlite'
])
param databaseProvider string = 'Sqlite'

@description('true = MSSQL only, authenticate via the App Service system-assigned managed identity instead of a connection-string password (the identity still needs to be granted access on the SQL side separately - this template does not do that).')
param useManagedIdentity bool = false

@secure()
@description('MSSQL connection string. Leave empty unless DatabaseProvider is MsSql.')
param connectionStringMsSql string = ''

@secure()
@description('MySQL connection string. Leave empty unless DatabaseProvider is MySql.')
param connectionStringMySql string = ''

@secure()
@description('PostgreSQL connection string. Leave empty unless DatabaseProvider is PostgreSql.')
param connectionStringPostgreSql string = ''

@secure()
@description('SQLite connection string (a relative file path is fine - see DatabaseProvider above for the ephemeral-storage caveat).')
param connectionStringSqlite string = 'Data Source=scaletrigger.db'

// --- Auth / Startup / Cache ---------------------------------------------

@description('true = POST /api/vote/add and other admin actions require a JWT from POST /api/auth/login.')
param authEnabled bool = false

@description('true = refuse to start if the database is unreachable at startup; false = log a critical error and keep running (dashboard shows "Database unavailable" and keeps retrying).')
param failFastOnDbCheck bool = false

@description('true = cache GET /api/vote/report in memory; false = every read goes straight to the database.')
param cacheEnabled bool = true

param cacheSlidingExpirationMinutes int = 5

// --- Load:* (seeds LoadConfig the first time it's created - edit live afterwards) ---

param loadConfigRefreshMinSeconds int = 1
param loadConfigRefreshMaxSeconds int = 1
param loadCpuIterationsPerVoteMin int = 5000
param loadCpuIterationsPerVoteMax int = 20000
param loadMemoryMegabytesPerVoteMin int = 3
param loadMemoryMegabytesPerVoteMax int = 8
param loadDiskWriteKilobytesPerVoteMin int = 32
param loadDiskWriteKilobytesPerVoteMax int = 96
param loadNetworkLatencyMillisecondsPerVoteMin int = 10
param loadNetworkLatencyMillisecondsPerVoteMax int = 30
param loadPayloadBytesPerVoteMin int = 0
param loadPayloadBytesPerVoteMax int = 0
param loadDbCpuIterationsPerVoteMin int = 0
param loadDbCpuIterationsPerVoteMax int = 0

// --- NodeBenchmark:* (one-off hardware benchmark, unrelated to per-vote load) ---

param nodeBenchmarkCpuDurationSeconds int = 20
param nodeBenchmarkMemoryBlockMegabytes int = 64
param nodeBenchmarkMemoryRepetitions int = 5
param nodeBenchmarkDiskSizeMegabytes int = 20
param nodeBenchmarkDiskRepetitions int = 5

// --- Jwt:* / AdminUser:* -------------------------------------------------

@secure()
@description('JWT signing key, at least 32 characters. The appsettings.json.example placeholder is reused as the default since this is a stress-test tool with no real secrets to protect by default - replace it if that ever stops being true.')
param jwtKey string = 'abcdefghijklmnopqrstuvwxyz012345'

param jwtIssuer string = 'ScaleTrigger'
param jwtAudience string = 'ScaleTrigger.Clients'
param jwtExpirationMinutes int = 60

@description('POST /api/auth/login username.')
param adminUsername string = 'admin'

@secure()
@description('POST /api/auth/login password. Default matches appsettings.json.example - change it if Auth:Enabled is ever true outside a throwaway environment.')
param adminPassword string = 'admin'

// --- Logging / AllowedHosts ----------------------------------------------

param loggingLevelDefault string = 'Information'
param loggingLevelAspNetCore string = 'Warning'
param allowedHosts string = '*'

// -------------------------------------------------------------------------

var appServicePlanName = '${appServiceName}-plan'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: {
    name: skuName
  }
  properties: {
    reserved: true
  }
}

resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'true' }

        { name: 'DatabaseProvider', value: databaseProvider }
        { name: 'UseManagedIdentity', value: string(useManagedIdentity) }

        { name: 'ConnectionStrings__MsSql', value: connectionStringMsSql }
        { name: 'ConnectionStrings__MySql', value: connectionStringMySql }
        { name: 'ConnectionStrings__PostgreSql', value: connectionStringPostgreSql }
        { name: 'ConnectionStrings__Sqlite', value: connectionStringSqlite }

        { name: 'Auth__Enabled', value: string(authEnabled) }
        { name: 'Startup__FailFastOnDbCheck', value: string(failFastOnDbCheck) }
        { name: 'Cache__Enabled', value: string(cacheEnabled) }
        { name: 'Cache__SlidingExpirationMinutes', value: string(cacheSlidingExpirationMinutes) }

        { name: 'Load__ConfigRefresh__Min', value: string(loadConfigRefreshMinSeconds) }
        { name: 'Load__ConfigRefresh__Max', value: string(loadConfigRefreshMaxSeconds) }
        { name: 'Load__CpuIterationsPerVote__Min', value: string(loadCpuIterationsPerVoteMin) }
        { name: 'Load__CpuIterationsPerVote__Max', value: string(loadCpuIterationsPerVoteMax) }
        { name: 'Load__MemoryMegabytesPerVote__Min', value: string(loadMemoryMegabytesPerVoteMin) }
        { name: 'Load__MemoryMegabytesPerVote__Max', value: string(loadMemoryMegabytesPerVoteMax) }
        { name: 'Load__DiskWriteKilobytesPerVote__Min', value: string(loadDiskWriteKilobytesPerVoteMin) }
        { name: 'Load__DiskWriteKilobytesPerVote__Max', value: string(loadDiskWriteKilobytesPerVoteMax) }
        { name: 'Load__NetworkLatencyMillisecondsPerVote__Min', value: string(loadNetworkLatencyMillisecondsPerVoteMin) }
        { name: 'Load__NetworkLatencyMillisecondsPerVote__Max', value: string(loadNetworkLatencyMillisecondsPerVoteMax) }
        { name: 'Load__PayloadBytesPerVote__Min', value: string(loadPayloadBytesPerVoteMin) }
        { name: 'Load__PayloadBytesPerVote__Max', value: string(loadPayloadBytesPerVoteMax) }
        { name: 'Load__DbCpuIterationsPerVote__Min', value: string(loadDbCpuIterationsPerVoteMin) }
        { name: 'Load__DbCpuIterationsPerVote__Max', value: string(loadDbCpuIterationsPerVoteMax) }

        { name: 'NodeBenchmark__CpuDurationSeconds', value: string(nodeBenchmarkCpuDurationSeconds) }
        { name: 'NodeBenchmark__MemoryBlockMegabytes', value: string(nodeBenchmarkMemoryBlockMegabytes) }
        { name: 'NodeBenchmark__MemoryRepetitions', value: string(nodeBenchmarkMemoryRepetitions) }
        { name: 'NodeBenchmark__DiskSizeMegabytes', value: string(nodeBenchmarkDiskSizeMegabytes) }
        { name: 'NodeBenchmark__DiskRepetitions', value: string(nodeBenchmarkDiskRepetitions) }

        { name: 'Jwt__Key', value: jwtKey }
        { name: 'Jwt__Issuer', value: jwtIssuer }
        { name: 'Jwt__Audience', value: jwtAudience }
        { name: 'Jwt__ExpirationMinutes', value: string(jwtExpirationMinutes) }

        { name: 'AdminUser__Username', value: adminUsername }
        { name: 'AdminUser__Password', value: adminPassword }

        { name: 'Logging__LogLevel__Default', value: loggingLevelDefault }
        { name: 'Logging__LogLevel__Microsoft.AspNetCore', value: loggingLevelAspNetCore }
        { name: 'AllowedHosts', value: allowedHosts }
      ]
    }
  }
}

resource sourceControl 'Microsoft.Web/sites/sourcecontrols@2023-12-01' = {
  parent: appService
  name: 'web'
  properties: {
    repoUrl: repositoryUrl
    branch: branch
    isManualIntegration: true
  }
}

output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output appServiceName string = appService.name
