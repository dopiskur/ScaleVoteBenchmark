targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param adminUsername string = 'demoadmin'

@secure()
param adminPassword string

param logAnalyticsResourceGroupPrefix string = 'ScaleTriggerDemo'
param sqlResourceGroupPrefix string = 'ScaleTriggerDemo'

var resourceGroupName = '${resourceGroupPrefix}-ServicePlan'
var logAnalyticsResourceGroupName = '${logAnalyticsResourceGroupPrefix}-Logs'
var sqlResourceGroupName = '${sqlResourceGroupPrefix}-Database'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

var logAnalyticsWorkspaceId = resourceId(subscription().subscriptionId, logAnalyticsResourceGroupName, 'Microsoft.OperationalInsights/workspaces', '${resourcePrefix}-logs')

module servicePlan 'modules/service-plan.bicep' = {
  name: 'deploy-service-plan'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    sqlResourceGroupName: sqlResourceGroupName
    adminUsername: adminUsername
    adminPassword: adminPassword
  }
}

output appServiceUrl string = servicePlan.outputs.appServiceUrl
output resourceGroupName string = resourceGroupName
