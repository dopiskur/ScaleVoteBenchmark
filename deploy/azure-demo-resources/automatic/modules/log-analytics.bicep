param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param retentionInDays int = 30

@description('Seconds to wait after enabling the VM Insights solution before anything creates a data collection rule against this workspace. The solution resource reports success in ARM well before the InsightsMetrics table it provisions is actually queryable - this is a fixed wait, not a poll, because confirming table readiness would need an authenticated identity/RBAC for no real gain in reliability over a generous fixed delay.')
param vmInsightsPropagationWaitSeconds int = 600

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
// VMInsights tables) up front.
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

// vmInsightsSolution reporting "Succeeded" in ARM does not mean InsightsMetrics is
// queryable yet - that table's actual creation happens asynchronously behind the
// solution resource and has been observed to lag by several minutes, which fails DCR
// creation in single-vm.bicep/scale-set.bicep with "InvalidOutputTable" even though the
// dependency order above is already correct. This resource exists purely to burn time;
// it needs no identity because it makes no Azure API calls. Anything that consumes
// workspaceResourceId already waits for this whole module (including this resource), so
// no extra dependsOn is needed on the DCR side.
resource waitForVmInsightsTables 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: 'wait-for-vminsights-tables'
  location: location
  kind: 'AzurePowerShell'
  properties: {
    azPowerShellVersion: '14.0'
    retentionInterval: 'PT1H'
    cleanupPreference: 'OnSuccess'
    timeout: 'PT20M'
    arguments: '-waitSeconds ${vmInsightsPropagationWaitSeconds}'
    scriptContent: '''
      param([int] $waitSeconds)
      Write-Host "Waiting $waitSeconds seconds for the VM Insights solution to finish provisioning the InsightsMetrics table before any data collection rule references it."
      Start-Sleep -Seconds $waitSeconds
    '''
  }
  dependsOn: [
    vmInsightsSolution
  ]
}

output workspaceResourceId string = workspace.id
output workspaceName string = workspace.name
