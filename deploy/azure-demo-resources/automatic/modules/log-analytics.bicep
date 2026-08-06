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

// The VM/VMSS scenarios' data collection rules stream 'Microsoft-InsightsMetrics' into
// the built-in InsightsMetrics table, which a freshly created workspace doesn't have -
// DCR creation is validated against the table existing already, not created lazily on
// first ingestion. Enabling the VM Insights solution provisions it (and the other
// VMInsights tables) up front. Anything that consumes workspaceResourceId already waits
// for this whole module, so no extra dependsOn is needed on the DCR side.
resource vmInsightsSolution 'Microsoft.OperationsManagement/solutions@2015-11-01-preview' = {
  name: 'VMInsights(${workspace.name})'
  location: location
  plan: {
    name: 'VMInsights(${workspace.name})'
    product: 'OMSGallery/VMInsights'
    publisher: 'Microsoft'
    promotionCode: ''
  }
  properties: {
    workspaceResourceId: workspace.id
  }
}

output workspaceResourceId string = workspace.id
output workspaceName string = workspace.name
