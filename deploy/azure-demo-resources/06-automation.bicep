targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param singleVmResourceGroupPrefix string = 'ScaleTriggerDemo'
param servicePlanResourceGroupPrefix string = 'ScaleTriggerDemo'
param approvalNotificationUpn string = 'dummy@somemail.com'
param autoShutdownEnabled bool = true

var resourceGroupName = '${resourceGroupPrefix}-Automation'
var singleVmResourceGroup = '${singleVmResourceGroupPrefix}-SingleVM'
var servicePlanResourceGroup = '${servicePlanResourceGroupPrefix}-ServicePlan'

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module automation 'modules/automation.bicep' = {
  name: 'deploy-automation'
  scope: rg
  params: {
    location: location
    resourcePrefix: resourcePrefix
    singleVmResourceGroup: singleVmResourceGroup
    servicePlanResourceGroup: servicePlanResourceGroup
    approvalNotificationUpn: approvalNotificationUpn
    autoShutdownEnabled: autoShutdownEnabled
  }
}

output logicAppVmName string = automation.outputs.logicAppVmName
output logicAppPlanName string = automation.outputs.logicAppPlanName
output automationAccountName string = automation.outputs.automationAccountName
output teardownRunbookName string = automation.outputs.teardownRunbookName
output stopVmssRunbookName string = automation.outputs.stopVmssRunbookNameOut
output resourceGroupName string = resourceGroupName
