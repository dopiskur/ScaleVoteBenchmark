param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param retentionInDays int = 30

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${resourcePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
  }
}

output workspaceResourceId string = workspace.id
output workspaceName string = workspace.name
