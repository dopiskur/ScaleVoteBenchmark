// ScaleTrigger Azure Scaling Demo - one-click deployment.
//
// Single subscription-scope template combining everything the manual/
// (Deploy.ps1-driven) flow deploys as seven separate templates: a single VM
// (vertical scaling), a VM Scale Set (horizontal scaling), an App Service
// (horizontal + approval-gated vertical scaling), an Azure SQL Serverless
// database (platform-native vertical scaling), a shared Log Analytics
// workspace with a monitoring dashboard, and the Automation Account/Logic
// Apps/alerts that drive the two vertical-scaling scenarios.
//
// Every module here is unchanged from manual/scripts/modules - the only
// rewritten piece is modules/automation.bicep, where the manual flow's
// PowerShell follow-up (uploading + publishing runbook content, registering
// a schedule) is replaced with ARM-native equivalents (publishContentLink,
// Microsoft.Automation .../schedules + .../jobSchedules), so the whole thing
// finishes inside a single deployment with no script to run afterward.
//
// See README.md in this folder for the full walkthrough, cost breakdown,
// and first-deploy checklist. Compiled to main.json via `az bicep build` -
// see "Keeping main.json in sync" in that README.

targetScope = 'subscription'

@secure()
@description('Administrator password for the VM, VM Scale Set, and SQL Server. The only required parameter - must meet Azure password complexity rules (12+ characters, 3 of 4 character classes).')
param adminPassword string

@description('Administrator username for the VM, VMSS, and SQL Server.')
param adminUsername string = 'demoadmin'

@description('Prefix applied to all resource group names, e.g. "ScaleTriggerDemo-SingleVM".')
param resourceGroupPrefix string = 'ScaleTriggerDemo'

@description('Prefix applied to all resource names inside those groups, e.g. "ScaleTrigger-vm". Globally unique resources (the SQL Server and the Web App) additionally get a random suffix.')
param resourcePrefix string = 'ScaleTrigger'

@description('Azure region for every resource. Defaults to whatever region is picked in the deployment blade\'s own "Region" selector, so this field normally needs no separate input.')
param location string = deployment().location

@description('Azure AD account that receives a push notification (via the Azure mobile app) when the App Service plan approval-gated vertical scaling alert fires. The default will not notify anyone useful - replace it with a real account UPN.')
param approvalNotificationUpn string = 'dummy@somemail.com'

@description('UTC hour (0-23) the VM and VM Scale Set automatically shut down to limit cost when idle.')
param autoShutdownHour int = 5

@description('Whether to create the daily VM Scale Set auto-shutdown schedule.')
param autoShutdownEnabled bool = true

var logsResourceGroupName = '${resourceGroupPrefix}-Logs'
var singleVmResourceGroupName = '${resourceGroupPrefix}-SingleVM'
var scaleSetResourceGroupName = '${resourceGroupPrefix}-ScaleSet'
var servicePlanResourceGroupName = '${resourceGroupPrefix}-ServicePlan'
var sqlResourceGroupName = '${resourceGroupPrefix}-Database'
var automationResourceGroupName = '${resourceGroupPrefix}-Automation'
var autoShutdownTime = '${padLeft(string(autoShutdownHour), 2, '0')}00'

resource logsRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: logsResourceGroupName
  location: location
}

resource singleVmRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: singleVmResourceGroupName
  location: location
}

resource scaleSetRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: scaleSetResourceGroupName
  location: location
}

resource servicePlanRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: servicePlanResourceGroupName
  location: location
}

resource sqlRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: sqlResourceGroupName
  location: location
}

resource automationRg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: automationResourceGroupName
  location: location
}

module logAnalytics 'modules/log-analytics.bicep' = {
  name: 'deploy-log-analytics'
  scope: logsRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
  }
}

module sqlDatabase 'modules/sql-database.bicep' = {
  name: 'deploy-sql-database'
  scope: sqlRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    adminUsername: adminUsername
    adminPassword: adminPassword
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
  }
}

module singleVm 'modules/single-vm.bicep' = {
  name: 'deploy-single-vm'
  scope: singleVmRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    adminUsername: adminUsername
    adminPassword: adminPassword
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
    autoShutdownTime: autoShutdownTime
  }
}

module scaleSet 'modules/scale-set.bicep' = {
  name: 'deploy-scale-set'
  scope: scaleSetRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    adminUsername: adminUsername
    adminPassword: adminPassword
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
    sqlResourceGroupName: sqlResourceGroupName
  }
  dependsOn: [
    sqlDatabase
  ]
}

module servicePlan 'modules/service-plan.bicep' = {
  name: 'deploy-service-plan'
  scope: servicePlanRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
    sqlResourceGroupName: sqlResourceGroupName
    adminUsername: adminUsername
    adminPassword: adminPassword
  }
  dependsOn: [
    sqlDatabase
  ]
}

module automation 'modules/automation.bicep' = {
  name: 'deploy-automation'
  scope: automationRg
  params: {
    location: location
    resourcePrefix: resourcePrefix
    singleVmResourceGroup: singleVmResourceGroupName
    servicePlanResourceGroup: servicePlanResourceGroupName
    scaleSetResourceGroup: scaleSetResourceGroupName
    approvalNotificationUpn: approvalNotificationUpn
    autoShutdownEnabled: autoShutdownEnabled
    autoShutdownHour: autoShutdownHour
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
  }
  dependsOn: [
    singleVm
    servicePlan
    scaleSet
  ]
}

module dashboard 'modules/dashboard.bicep' = {
  name: 'deploy-dashboard'
  scope: logsRg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    vmResourceId: singleVm.outputs.vmResourceId
    vmssResourceId: scaleSet.outputs.vmssResourceId
    appServicePlanResourceId: servicePlan.outputs.appServicePlanResourceId
    sqlDatabaseResourceId: sqlDatabase.outputs.sqlDatabaseResourceId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceResourceId
    vmssAutoscaleResourceId: resourceId(subscription().subscriptionId, scaleSetResourceGroupName, 'Microsoft.Insights/autoscalesettings', 'autoscale-${resourcePrefix}-vmss')
    planAutoscaleResourceId: resourceId(subscription().subscriptionId, servicePlanResourceGroupName, 'Microsoft.Insights/autoscalesettings', 'autoscale-${servicePlan.outputs.appServicePlanName}')
    logicAppVmResourceId: resourceId(subscription().subscriptionId, automationResourceGroupName, 'Microsoft.Logic/workflows', '${resourcePrefix}-la-vm-resize')
    logicAppPlanResourceId: resourceId(subscription().subscriptionId, automationResourceGroupName, 'Microsoft.Logic/workflows', '${resourcePrefix}-la-plan-resize-approval')
  }
  dependsOn: [
    automation
  ]
}

output vmFqdn string = singleVm.outputs.vmFqdn
output vmssFqdn string = scaleSet.outputs.vmssFqdn
output appServiceUrl string = servicePlan.outputs.appServiceUrl
output sqlServerFqdn string = sqlDatabase.outputs.sqlServerFqdn
output dashboardResourceGroup string = logsResourceGroupName
