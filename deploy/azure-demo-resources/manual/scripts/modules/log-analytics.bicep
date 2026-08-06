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
// VMInsights tables) up front. Deploy.ps1 already runs module 01 (this one) to
// completion before modules 02/03 that create DCRs, so no extra ordering is needed here.
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
