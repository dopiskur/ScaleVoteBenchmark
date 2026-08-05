targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'

var resourceGroupName = '${resourceGroupPrefix}-Logs'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'deploy-log-analytics'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    location: location
  }
}

output workspaceResourceId string = logAnalytics.outputs.workspaceResourceId
output resourceGroupName string = resourceGroupName
