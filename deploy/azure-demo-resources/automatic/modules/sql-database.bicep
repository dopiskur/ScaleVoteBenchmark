param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param adminUsername string

@secure()
param adminPassword string

param maxVCores int = 4
param minVCores string = '0.5'
param autoPauseDelayMinutes int = 1440
param allowAzureServices bool = true
param backupStorageRedundancy string = 'Local'
param logAnalyticsWorkspaceId string

var sqlServerName = toLower('${resourcePrefix}-sqlserver-${uniqueString(resourceGroup().id)}')
var sqlDatabaseName = '${resourcePrefix}-sqldb'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: adminUsername
    administratorLoginPassword: adminPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
  }
}

resource allowAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (allowAzureServices) {
  parent: sqlServer
  name: 'AllowAllAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: maxVCores
  }
  properties: {
    minCapacity: json(minVCores)
    autoPauseDelay: autoPauseDelayMinutes
    zoneRedundant: false
    requestedBackupStorageRedundancy: backupStorageRedundancy
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: sqlDatabase
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output sqlDatabaseResourceId string = sqlDatabase.id
output sqlServerFqdn string = '${sqlServer.name}${environment().suffixes.sqlServerHostname}'
output sqlServerName string = sqlServer.name
output sqlDatabaseName string = sqlDatabase.name
