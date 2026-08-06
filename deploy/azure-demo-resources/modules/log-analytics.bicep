param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param retentionInDays int = 30

@description('Seconds to wait after enabling VM Insights before any DCR references the workspace - ARM reports success before InsightsMetrics is actually queryable.')
param vmInsightsPropagationWaitSeconds int = 600

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${resourcePrefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: retentionInDays
  }
}

// DCR creation is validated against InsightsMetrics existing already - enabling VM Insights provisions it up front.
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

// ARM reports "Succeeded" well before InsightsMetrics is actually queryable, failing DCRs with "InvalidOutputTable" - pure time-burner, no identity needed.
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
