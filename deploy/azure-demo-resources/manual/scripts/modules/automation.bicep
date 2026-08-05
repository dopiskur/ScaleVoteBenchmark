param location string = resourceGroup().location
param resourcePrefix string = 'ScaleTrigger'
param singleVmResourceGroup string
param servicePlanResourceGroup string
param approvalNotificationUpn string = 'dummy@somemail.com'
param vmCpuThreshold int = 80
param planCpuThreshold int = 80
param autoShutdownEnabled bool = true

var armEndpoint = environment().resourceManager
var vmName = '${resourcePrefix}-vm'
var vmResourceId = resourceId(subscription().subscriptionId, singleVmResourceGroup, 'Microsoft.Compute/virtualMachines', vmName)
var servicePlanResourceGroupId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${servicePlanResourceGroup}'
var webAppName = toLower('${resourcePrefix}-webapp-${uniqueString(servicePlanResourceGroupId)}')
var servicePlanName = '${webAppName}-plan'
var planResourceId = resourceId(subscription().subscriptionId, servicePlanResourceGroup, 'Microsoft.Web/serverfarms', servicePlanName)

resource logicAppVm 'Microsoft.Logic/workflows@2019-05-01' = {
  name: '${resourcePrefix}-la-vm-resize'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        manual: {
          type: 'Request'
          kind: 'Http'
          inputs: { schema: {} }
        }
      }
      actions: {
        CheckAlertState: {
          type: 'If'
          expression: {
            equals: [
              '@triggerBody()?[\'data\']?[\'essentials\']?[\'monitorCondition\']'
              'Fired'
            ]
          }
          actions: {
            ScaleUp: {
              type: 'Http'
              inputs: {
                method: 'PATCH'
                uri: uri(armEndpoint, '${vmResourceId}?api-version=2024-07-01')
                authentication: { type: 'ManagedServiceIdentity', audience: armEndpoint }
                headers: { 'Content-Type': 'application/json' }
                body: { properties: { hardwareProfile: { vmSize: 'Standard_B1ms' } } }
              }
            }
          }
          else: {
            actions: {
              ScaleDown: {
                type: 'Http'
                inputs: {
                  method: 'PATCH'
                  uri: uri(armEndpoint, '${vmResourceId}?api-version=2024-07-01')
                  authentication: { type: 'ManagedServiceIdentity', audience: armEndpoint }
                  headers: { 'Content-Type': 'application/json' }
                  body: { properties: { hardwareProfile: { vmSize: 'Standard_B1s' } } }
                }
              }
            }
          }
        }
      }
    }
  }
}

module vmRoleGrant 'grant-role-vm.bicep' = {
  name: 'grant-role-vm'
  scope: resourceGroup(singleVmResourceGroup)
  params: {
    vmName: vmName
    principalId: logicAppVm.identity.principalId
  }
}

resource actionGroupVm 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${resourcePrefix}-ag-vm'
  location: 'global'
  properties: {
    groupShortName: 'stVmScale'
    enabled: true
    logicAppReceivers: [
      {
        name: 'scaleTriggerLogicAppVm'
        resourceId: logicAppVm.id
        callbackUrl: listCallbackUrl('${logicAppVm.id}/triggers/manual', '2019-05-01').value
        useCommonAlertSchema: true
      }
    ]
  }
}

resource alertVm 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${resourcePrefix}-alert-vm-cpu-high'
  location: 'global'
  properties: {
    severity: 2
    enabled: true
    scopes: [vmResourceId]
    evaluationFrequency: 'PT1M'
    windowSize: 'PT5M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'HighCpu'
          metricNamespace: 'Microsoft.Compute/virtualMachines'
          metricName: 'Percentage CPU'
          operator: 'GreaterThan'
          threshold: vmCpuThreshold
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupVm.id } ]
    autoMitigate: true
  }
}

resource logicAppPlan 'Microsoft.Logic/workflows@2019-05-01' = {
  name: '${resourcePrefix}-la-plan-resize-approval'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        manual: {
          type: 'Request'
          kind: 'Http'
          inputs: { schema: {} }
        }
      }
      actions: {
        ScaleUpPlan: {
          type: 'Http'
          inputs: {
            method: 'PATCH'
            uri: uri(armEndpoint, '${planResourceId}?api-version=2023-12-01')
            authentication: { type: 'ManagedServiceIdentity', audience: armEndpoint }
            headers: { 'Content-Type': 'application/json' }
            body: { sku: { name: 'P1v3', tier: 'PremiumV3' } }
          }
        }
      }
    }
  }
}

module planRoleGrant 'grant-role-plan.bicep' = {
  name: 'grant-role-plan'
  scope: resourceGroup(servicePlanResourceGroup)
  params: {
    planName: servicePlanName
    principalId: logicAppPlan.identity.principalId
  }
}

resource actionGroupPlan 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: '${resourcePrefix}-ag-plan'
  location: 'global'
  properties: {
    groupShortName: 'stPlanEsc'
    enabled: true
    azureAppPushReceivers: [
      {
        name: 'approvalPush'
        emailAddress: approvalNotificationUpn
      }
    ]
  }
}

resource alertPlan 'Microsoft.Insights/metricAlerts@2018-03-01' = {
  name: '${resourcePrefix}-alert-plan-cpu-high-escalation'
  location: 'global'
  properties: {
    severity: 1
    enabled: true
    scopes: [planResourceId]
    evaluationFrequency: 'PT5M'
    windowSize: 'PT15M'
    criteria: {
      'odata.type': 'Microsoft.Azure.Monitor.SingleResourceMultipleMetricCriteria'
      allOf: [
        {
          criterionType: 'StaticThresholdCriterion'
          name: 'SustainedHighCpu'
          metricNamespace: 'Microsoft.Web/serverfarms'
          metricName: 'CpuPercentage'
          operator: 'GreaterThan'
          threshold: planCpuThreshold
          timeAggregation: 'Average'
        }
      ]
    }
    actions: [ { actionGroupId: actionGroupPlan.id } ]
    autoMitigate: true
  }
}

resource automationAccount 'Microsoft.Automation/automationAccounts@2023-11-01' = {
  name: '${resourcePrefix}-automation'
  location: location
  identity: { type: 'SystemAssigned' }
  properties: {
    sku: { name: 'Free' }
  }
}

resource teardownRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = {
  parent: automationAccount
  name: 'Remove-DemoResources'
  location: location
  properties: {
    runbookType: 'PowerShell'
    logProgress: false
    logVerbose: false
  }
}

resource stopVmssRunbook 'Microsoft.Automation/automationAccounts/runbooks@2023-11-01' = if (autoShutdownEnabled) {
  parent: automationAccount
  name: 'Stop-ScaleSetInstances'
  location: location
  properties: {
    runbookType: 'PowerShell'
    logProgress: false
    logVerbose: false
  }
}

module automationAccountRoleGrant 'grant-role-subscription.bicep' = {
  name: 'grant-role-automation-account'
  scope: subscription()
  params: {
    principalId: automationAccount.identity.principalId
  }
}

output logicAppVmName string = logicAppVm.name
output logicAppPlanName string = logicAppPlan.name
output automationAccountName string = automationAccount.name
output teardownRunbookName string = teardownRunbook.name
output stopVmssRunbookNameOut string = autoShutdownEnabled ? 'Stop-ScaleSetInstances' : ''
#disable-next-line outputs-should-not-contain-secrets
output logicAppPlanTriggerUrl string = listCallbackUrl('${logicAppPlan.id}/triggers/manual', '2019-05-01').value
