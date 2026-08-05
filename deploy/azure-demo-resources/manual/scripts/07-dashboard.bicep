targetScope = 'subscription'

param resourceGroupPrefix string = 'ScaleTriggerDemo'
param resourcePrefix string = 'ScaleTrigger'
param location string = 'eastus'
param singleVmResourceGroupPrefix string = 'ScaleTriggerDemo'
param scaleSetResourceGroupPrefix string = 'ScaleTriggerDemo'
param servicePlanResourceGroupPrefix string = 'ScaleTriggerDemo'
param sqlResourceGroupPrefix string = 'ScaleTriggerDemo'
param logAnalyticsResourceGroupPrefix string = 'ScaleTriggerDemo'
param automationResourceGroupPrefix string = 'ScaleTriggerDemo'
param vmResizeThreshold int = 80
param vmssScaleOutThreshold int = 80
param vmssScaleInThreshold int = 30
param planScaleOutThreshold int = 80
param planScaleInThreshold int = 30

var resourceGroupName = '${resourceGroupPrefix}-Logs'
var logAnalyticsResourceGroupName = '${logAnalyticsResourceGroupPrefix}-Logs'
var singleVmResourceGroupName = '${singleVmResourceGroupPrefix}-SingleVM'
var scaleSetResourceGroupName = '${scaleSetResourceGroupPrefix}-ScaleSet'
var servicePlanResourceGroupName = '${servicePlanResourceGroupPrefix}-ServicePlan'
var sqlResourceGroupName = '${sqlResourceGroupPrefix}-Database'
var automationResourceGroupName = '${automationResourceGroupPrefix}-Automation'

var vmName = '${resourcePrefix}-vm'
var vmssName = '${resourcePrefix}-vmss'

var servicePlanResourceGroupId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${servicePlanResourceGroupName}'
var webAppName = toLower('${resourcePrefix}-webapp-${uniqueString(servicePlanResourceGroupId)}')
var appServicePlanName = '${webAppName}-plan'

var sqlResourceGroupId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${sqlResourceGroupName}'
var sqlServerName = toLower('${resourcePrefix}-sqlserver-${uniqueString(sqlResourceGroupId)}')
var sqlDatabaseName = '${resourcePrefix}-sqldb'

var vmResourceId = resourceId(subscription().subscriptionId, singleVmResourceGroupName, 'Microsoft.Compute/virtualMachines', vmName)
var vmssResourceId = resourceId(subscription().subscriptionId, scaleSetResourceGroupName, 'Microsoft.Compute/virtualMachineScaleSets', vmssName)
var appServicePlanResourceId = resourceId(subscription().subscriptionId, servicePlanResourceGroupName, 'Microsoft.Web/serverfarms', appServicePlanName)
var webAppResourceId = resourceId(subscription().subscriptionId, servicePlanResourceGroupName, 'Microsoft.Web/sites', webAppName)
var sqlDatabaseResourceId = resourceId(subscription().subscriptionId, sqlResourceGroupName, 'Microsoft.Sql/servers/databases', sqlServerName, sqlDatabaseName)
var logAnalyticsWorkspaceId = resourceId(subscription().subscriptionId, logAnalyticsResourceGroupName, 'Microsoft.OperationalInsights/workspaces', '${resourcePrefix}-logs')
var vmssAutoscaleResourceId = resourceId(subscription().subscriptionId, scaleSetResourceGroupName, 'Microsoft.Insights/autoscalesettings', 'autoscale-${vmssName}')
var planAutoscaleResourceId = resourceId(subscription().subscriptionId, servicePlanResourceGroupName, 'Microsoft.Insights/autoscalesettings', 'autoscale-${appServicePlanName}')
var logicAppVmResourceId = resourceId(subscription().subscriptionId, automationResourceGroupName, 'Microsoft.Logic/workflows', '${resourcePrefix}-la-vm-resize')
var logicAppPlanResourceId = resourceId(subscription().subscriptionId, automationResourceGroupName, 'Microsoft.Logic/workflows', '${resourcePrefix}-la-plan-resize-approval')

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
}

module dashboard 'modules/dashboard.bicep' = {
  name: 'deploy-dashboard'
  scope: rg
  params: {
    resourcePrefix: resourcePrefix
    location: location
    vmResourceId: vmResourceId
    vmssResourceId: vmssResourceId
    appServicePlanResourceId: appServicePlanResourceId
    webAppResourceId: webAppResourceId
    sqlDatabaseResourceId: sqlDatabaseResourceId
    logAnalyticsWorkspaceId: logAnalyticsWorkspaceId
    vmssAutoscaleResourceId: vmssAutoscaleResourceId
    planAutoscaleResourceId: planAutoscaleResourceId
    logicAppVmResourceId: logicAppVmResourceId
    logicAppPlanResourceId: logicAppPlanResourceId
    vmResizeThreshold: vmResizeThreshold
    vmssScaleOutThreshold: vmssScaleOutThreshold
    vmssScaleInThreshold: vmssScaleInThreshold
    planScaleOutThreshold: planScaleOutThreshold
    planScaleInThreshold: planScaleInThreshold
  }
}

output workbookName string = dashboard.outputs.workbookName
output resourceGroupName string = resourceGroupName
