targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param adminUsername string = 'demoadmin'

@secure()
param adminPassword string

param logAnalyticsResourceGroupPrefix string = 'ScaleTriggerDemo'
param autoShutdownTime string = '0500'
param autoShutdownTimeZone string = 'Central European Standard Time'

var resourceGroupName = '${resourceGroupPrefix}-SingleVM'
var logAnalyticsResourceGroupName = '${logAnalyticsResourceGroupPrefix}-Logs'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

var logAnalyticsWorkspaceId = resourceId(subscription().subscriptionId, logAnalyticsResourceGroupName, 'Microsoft.OperationalInsights/workspaces', '${resourcePrefix}-logs')

module singleVm 'modules/single-vm.bicep' = {
  name: 'deploy-single-vm'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    adminUsername: adminUsername
    adminPassword: adminPassword
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    autoShutdownTime: autoShutdownTime
    autoShutdownTimeZone: autoShutdownTimeZone
  }
}

output vmFqdn string = singleVm.outputs.vmFqdn
output resourceGroupName string = resourceGroupName
