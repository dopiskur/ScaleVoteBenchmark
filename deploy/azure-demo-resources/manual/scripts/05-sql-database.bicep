targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param adminUsername string = 'demoadmin'

@secure()
param adminPassword string

param logAnalyticsResourceGroupPrefix string = 'ScaleTriggerDemo'

var resourceGroupName = '${resourceGroupPrefix}-Database'
var logAnalyticsResourceGroupName = '${logAnalyticsResourceGroupPrefix}-Logs'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

var logAnalyticsWorkspaceId = resourceId(subscription().subscriptionId, logAnalyticsResourceGroupName, 'Microsoft.OperationalInsights/workspaces', '${resourcePrefix}-logs')

module sqlDatabase 'modules/sql-database.bicep' = {
  name: 'deploy-sql-database'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    adminUsername: adminUsername
    adminPassword: adminPassword
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
  }
}

output sqlServerFqdn string = sqlDatabase.outputs.sqlServerFqdn
output resourceGroupName string = resourceGroupName
