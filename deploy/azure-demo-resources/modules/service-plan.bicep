param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location

@allowed(['P0v3', 'P1v3', 'P2v3', 'P3v3'])
param skuName string = 'P0v3'

param repositoryUrl string = 'https://github.com/dopiskur/scaleTrigger'
param branch string = 'master'
param baseCapacity int = 1
param maxCapacity int = 4
param scaleOutThreshold int = 80
param scaleInThreshold int = 30
param cooldownMinutes int = 2
param logAnalyticsWorkspaceId string
param sqlResourceGroupName string
param adminUsername string

@secure()
param adminPassword string

var webAppName = toLower('${resourcePrefix}-webapp-${uniqueString(resourceGroup().id)}')
var appServicePlanName = '${webAppName}-plan'
var sqlResourceGroupId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${sqlResourceGroupName}'
var sqlServerName = toLower('${resourcePrefix}-sqlserver-${uniqueString(sqlResourceGroupId)}')
var sqlDatabaseName = '${resourcePrefix}-sqldb'
var sqlServerFqdn = '${sqlServerName}${environment().suffixes.sqlServerHostname}'

resource appServicePlan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: appServicePlanName
  location: location
  kind: 'linux'
  sku: { name: skuName }
  properties: { reserved: true }
}

resource appService 'Microsoft.Web/sites@2023-12-01' = {
  name: webAppName
  location: location
  kind: 'app,linux'
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      appSettings: [
        { name: 'SCM_DO_BUILD_DURING_DEPLOYMENT', value: 'true' }
        { name: 'DatabaseProvider', value: 'MsSql' }
        { name: 'ConnectionStrings__MsSql', value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};User ID=${adminUsername};Password=${adminPassword};Encrypt=True;TrustServerCertificate=False;' }
        { name: 'Auth__Enabled', value: 'true' }
        { name: 'AdminUser__Username', value: adminUsername }
        { name: 'AdminUser__Password', value: adminPassword }
        { name: 'Startup__FailFastOnDbCheck', value: 'false' }
        { name: 'Jwt__Key', value: 'abcdefghijklmnopqrstuvwxyz012345' }
        { name: 'Jwt__Issuer', value: 'ScaleTrigger' }
        { name: 'Jwt__Audience', value: 'ScaleTrigger.Clients' }
        { name: 'Jwt__ExpirationMinutes', value: '60' }
        { name: 'Cache__SlidingExpirationMinutes', value: '5' }
        { name: 'Load__ConfigRefresh__Min', value: '1' }
        { name: 'Load__ConfigRefresh__Max', value: '1' }
        { name: 'Load__LoadEnabled__Min', value: '1' }
        { name: 'Load__LoadEnabled__Max', value: '1' }
        { name: 'Load__CacheEnabled__Min', value: '1' }
        { name: 'Load__CacheEnabled__Max', value: '1' }
        { name: 'Load__CpuIterationsPerVote__Min', value: '100000' }
        { name: 'Load__CpuIterationsPerVote__Max', value: '500000' }
        { name: 'Load__MemoryKilobytesPerVote__Min', value: '3072' }
        { name: 'Load__MemoryKilobytesPerVote__Max', value: '8192' }
        { name: 'Load__DiskWriteKilobytesPerVote__Min', value: '32' }
        { name: 'Load__DiskWriteKilobytesPerVote__Max', value: '96' }
        { name: 'Load__NetworkLatencyMillisecondsPerVote__Min', value: '10' }
        { name: 'Load__NetworkLatencyMillisecondsPerVote__Max', value: '30' }
        { name: 'Load__PayloadBytesPerVote__Min', value: '0' }
        { name: 'Load__PayloadBytesPerVote__Max', value: '0' }
        { name: 'Load__DbCpuIterationsPerVote__Min', value: '0' }
        { name: 'Load__DbCpuIterationsPerVote__Max', value: '0' }
        { name: 'NodeBenchmark__CpuDurationSeconds', value: '20' }
        { name: 'NodeBenchmark__MemoryBlockMegabytes', value: '64' }
        { name: 'NodeBenchmark__MemoryRepetitions', value: '5' }
        { name: 'NodeBenchmark__DiskSizeMegabytes', value: '20' }
        { name: 'NodeBenchmark__DiskRepetitions', value: '5' }
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

resource autoscale 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: 'autoscale-${appServicePlanName}'
  location: location
  properties: {
    enabled: true
    targetResourceUri: appServicePlan.id
    profiles: [
      {
        name: 'default'
        capacity: { minimum: string(baseCapacity), maximum: string(maxCapacity), default: string(baseCapacity) }
        rules: [
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT1M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: scaleOutThreshold
            }
            scaleAction: { direction: 'Increase', type: 'ChangeCount', value: '1', cooldown: 'PT${cooldownMinutes}M' }
          }
          {
            metricTrigger: {
              metricName: 'CpuPercentage'
              metricResourceUri: appServicePlan.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT1M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: scaleInThreshold
            }
            scaleAction: { direction: 'Decrease', type: 'ChangeCount', value: '1', cooldown: 'PT${cooldownMinutes}M' }
          }
        ]
      }
    ]
  }
}

resource diagnosticsPlan 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: appServicePlan
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

resource autoscaleDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: autoscale
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logAnalyticsDestinationType: 'Dedicated'
    logs: [
      { category: 'AutoscaleEvaluations', enabled: true }
      { category: 'AutoscaleScaleActions', enabled: true }
    ]
  }
}

resource diagnosticsSite 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: appService
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [ { category: 'AllMetrics', enabled: true } ]
  }
}

output appServicePlanResourceId string = appServicePlan.id
output appServicePlanName string = appServicePlan.name
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output webAppName string = appService.name
