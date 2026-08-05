targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param adminUsername string = 'demoadmin'

@secure()
param adminPassword string

param logAnalyticsResourceGroupPrefix string = 'ScaleTriggerDemo'
param sqlResourceGroupPrefix string = 'ScaleTriggerDemo'

var resourceGroupName = '${resourceGroupPrefix}-ScaleSet'
var logAnalyticsResourceGroupName = '${logAnalyticsResourceGroupPrefix}-Logs'
var sqlResourceGroupName = '${sqlResourceGroupPrefix}-Database'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

var logAnalyticsWorkspaceId = resourceId(subscription().subscriptionId, logAnalyticsResourceGroupName, 'Microsoft.OperationalInsights/workspaces', '${resourcePrefix}-logs')

module scaleSet 'modules/scale-set.bicep' = {
  name: 'deploy-scale-set'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    adminUsername: adminUsername
    adminPassword: adminPassword
    location: location
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    sqlResourceGroupName: sqlResourceGroupName
  }
}

output vmssFqdn string = scaleSet.outputs.vmssFqdn
output resourceGroupName string = resourceGroupName
