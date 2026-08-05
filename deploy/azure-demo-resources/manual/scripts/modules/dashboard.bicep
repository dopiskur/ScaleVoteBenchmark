param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param vmResourceId string
param vmssResourceId string
param appServicePlanResourceId string
param webAppResourceId string
param sqlDatabaseResourceId string
param logAnalyticsWorkspaceId string
param vmssAutoscaleResourceId string
param planAutoscaleResourceId string
param logicAppVmResourceId string
param logicAppPlanResourceId string
param vmResizeThreshold int = 80
param vmssScaleOutThreshold int = 80
param vmssScaleInThreshold int = 30
param planScaleOutThreshold int = 80
param planScaleInThreshold int = 30

func textItem(itemName string, text string) object => {
  type: 1
  name: itemName
  content: {
    json: text
  }
}

func kqlChartItem(itemName string, title string, query string, workspaceId string) object => {
  type: 3
  name: itemName
  content: {
    version: 'KqlItem/1.0'
    query: query
    size: 0
    title: title
    queryType: 0
    resourceType: 'microsoft.operationalinsights/workspaces'
    crossComponentResources: [workspaceId]
    visualization: 'linechart'
  }
}

func argTableItem(itemName string, title string, query string) object => {
  type: 3
  name: itemName
  content: {
    version: 'KqlItem/1.0'
    query: query
    size: 0
    title: title
    queryType: 1
    resourceType: 'microsoft.resourcegraph/resources'
    visualization: 'table'
  }
}

func kqlTableItem(itemName string, title string, query string, workspaceId string) object => {
  type: 3
  name: itemName
  content: {
    version: 'KqlItem/1.0'
    query: query
    size: 0
    title: title
    queryType: 0
    resourceType: 'microsoft.operationalinsights/workspaces'
    crossComponentResources: [workspaceId]
    visualization: 'table'
  }
}

func azureMetricAvgQuery(resourceId string, metricName string) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Average) by bin(TimeGenerated, 1m) | render timechart'

func azureMetricTotalQuery(resourceId string, metricName string) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Total) by bin(TimeGenerated, 1m) | render timechart'

func azureMetricAvgWithThresholdQuery(resourceId string, metricName string, threshold int) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Average) by bin(TimeGenerated, 1m) | extend Threshold=${string(threshold)} | render timechart'

func azureMetricAvgWithThresholdsQuery(resourceId string, metricName string, scaleOutThreshold int, scaleInThreshold int) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Average) by bin(TimeGenerated, 1m) | extend ScaleOutThreshold=${string(scaleOutThreshold)}, ScaleInThreshold=${string(scaleInThreshold)} | render timechart'

func autoscaleActionsQuery(autoscaleResourceId string) string => 'AutoscaleScaleActionsLog | where ResourceId =~ "${autoscaleResourceId}" | project TimeGenerated, OperationName, ResultType, Properties | order by TimeGenerated desc | take 20'

func logicAppRunsQuery(logicAppResourceId string) string => 'AzureDiagnostics | where ResourceId =~ "${logicAppResourceId}" | where Category == "WorkflowRuntime" | project TimeGenerated, OperationName, ResultType, Level | order by TimeGenerated desc | take 20'

func insightsMetricQuery(resourceId string, namespaceName string, counterName string, perInstance bool) string => perInstance
  ? 'InsightsMetrics | where _ResourceId =~ "${resourceId}" | where Namespace == "${namespaceName}" and Name == "${counterName}" | summarize Value=avg(Val) by bin(TimeGenerated, 1m), Computer | render timechart'
  : 'InsightsMetrics | where _ResourceId =~ "${resourceId}" | where Namespace == "${namespaceName}" and Name == "${counterName}" | summarize Value=avg(Val) by bin(TimeGenerated, 1m) | render timechart'

var vmItems = [
  textItem('vm-header', '## VM (${resourcePrefix}-vm) - vertical scaling (Azure Monitor + Logic App resizes B1s -> B1ms)')
  argTableItem('vm-size', 'Current VM size', 'Resources | where id =~ "${vmResourceId}" | project Name=name, VmSize=tostring(properties.hardwareProfile.vmSize)')
  kqlTableItem('vm-resize-history', 'Resize Logic App - recent runs', logicAppRunsQuery(logicAppVmResourceId), logAnalyticsWorkspaceId)
  kqlChartItem('vm-cpu', 'CPU % (dashed line = resize threshold)', azureMetricAvgWithThresholdQuery(vmResourceId, 'Percentage CPU', vmResizeThreshold), logAnalyticsWorkspaceId)
  kqlChartItem('vm-mem', 'Memory % used', insightsMetricQuery(vmResourceId, 'Memory', '% Used Memory', false), logAnalyticsWorkspaceId)
  kqlChartItem('vm-disk', 'Disk bytes/min (read+write)', azureMetricTotalQuery(vmResourceId, 'Disk Read Bytes'), logAnalyticsWorkspaceId)
  textItem('vm-note', 'Single VM: no instance count tile - this scenario demonstrates vertical (resize) scaling only. Watch the "Current VM size" tile change from Standard_B1s to Standard_B1ms (and back) as the Logic App reacts to the CPU alert; the CPU chart plots the resize threshold alongside actual CPU, and the run-history table shows when the Logic App actually fired.')
]

var vmssItems = [
  textItem('vmss-header', '## VM Scale Set (${resourcePrefix}-vmss) - horizontal scaling (native Autoscale)')
  argTableItem('vmss-size', 'Current VM size (SKU)', 'Resources | where id =~ "${vmssResourceId}" | project Name=name, VmSize=tostring(sku.name)')
  argTableItem('vmss-instances', 'Current instance count', 'Resources | where id =~ "${vmssResourceId}" | project Name=name, InstanceCount=toint(sku.capacity)')
  kqlTableItem('vmss-scale-events', 'Autoscale - recent scale actions', autoscaleActionsQuery(vmssAutoscaleResourceId), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-cpu-avg', 'CPU % - average across all instances (dashed lines = scale-out/in thresholds)', azureMetricAvgWithThresholdsQuery(vmssResourceId, 'Percentage CPU', vmssScaleOutThreshold, vmssScaleInThreshold), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-cpu-per-instance', 'CPU % - per instance', insightsMetricQuery(vmssResourceId, 'Processor', 'PercentProcessorTime', true), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-mem-avg', 'Memory % used - average across all instances', insightsMetricQuery(vmssResourceId, 'Memory', '% Used Memory', false), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-mem-per-instance', 'Memory % used - per instance', insightsMetricQuery(vmssResourceId, 'Memory', '% Used Memory', true), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-disk', 'Disk read bytes/min (aggregate)', azureMetricTotalQuery(vmssResourceId, 'Disk Read Bytes'), logAnalyticsWorkspaceId)
]

var servicePlanItems = [
  textItem('plan-header', '## App Service (${resourcePrefix}-webapp) - horizontal Autoscale + approval-gated vertical scaling')
  argTableItem('plan-size', 'Current App Service Plan SKU', 'Resources | where id =~ "${appServicePlanResourceId}" | project Name=name, Sku=tostring(sku.name)')
  argTableItem('plan-instances', 'Current instance (worker) count', 'Resources | where id =~ "${appServicePlanResourceId}" | project Name=name, InstanceCount=toint(properties.numberOfWorkers)')
  kqlTableItem('plan-scale-events', 'Autoscale - recent scale actions', autoscaleActionsQuery(planAutoscaleResourceId), logAnalyticsWorkspaceId)
  kqlTableItem('plan-resize-history', 'Approval-gated resize Logic App - recent runs', logicAppRunsQuery(logicAppPlanResourceId), logAnalyticsWorkspaceId)
  kqlChartItem('plan-cpu', 'CPU % (dashed lines = scale-out/in thresholds)', azureMetricAvgWithThresholdsQuery(appServicePlanResourceId, 'CpuPercentage', planScaleOutThreshold, planScaleInThreshold), logAnalyticsWorkspaceId)
  kqlChartItem('plan-mem', 'Memory %', azureMetricAvgQuery(appServicePlanResourceId, 'MemoryPercentage'), logAnalyticsWorkspaceId)
  kqlChartItem('plan-disk', 'Disk queue length', azureMetricAvgQuery(appServicePlanResourceId, 'DiskQueueLength'), logAnalyticsWorkspaceId)
]

var sqlItems = [
  textItem('sql-header', '## Azure SQL Database (${resourcePrefix}-sqldb) - Serverless, platform-native vertical scaling')
  argTableItem('sql-vcores', 'Configured vCore range (min-max)', 'Resources | where id =~ "${sqlDatabaseResourceId}" | project Name=name, MaxVCores=toint(sku.capacity), MinVCores=todouble(properties.minCapacity)')
  kqlChartItem('sql-cpu', 'CPU %', azureMetricAvgQuery(sqlDatabaseResourceId, 'cpu_percent'), logAnalyticsWorkspaceId)
  kqlChartItem('sql-storage', 'Storage %', azureMetricAvgQuery(sqlDatabaseResourceId, 'storage_percent'), logAnalyticsWorkspaceId)
  textItem('sql-note', 'Serverless SQL DB has no "instance count" (it is a single logical database that scales vCores up/down, not out) and exposes no network metric. The vCore tile shows the configured min/max envelope (from the database resource properties) - Azure does not expose a metric for the exact vCore allocation active at a given moment, only CPU % relative to the max.')
]

var workbookDefinition = {
  version: 'Notebook/1.0'
  items: concat(
    [textItem('main-header', '# ${resourcePrefix} scaling dashboard\nCPU/memory/disk/network and instance counts across all four scaling scenarios. Charts default to a 1-hour window; use each chart\'s own time-range picker to widen it, and the workbook toolbar\'s Auto refresh control (top-right) to refresh live - Azure Monitor metrics land roughly once a minute, so a 1-5 minute auto-refresh is a reasonable match.')],
    vmItems,
    vmssItems,
    servicePlanItems,
    sqlItems
  )
  isLocked: false
  fallbackResourceIds: [logAnalyticsWorkspaceId]
}

resource workbook 'Microsoft.Insights/workbooks@2023-06-01' = {
  name: guid(resourceGroup().id, '${resourcePrefix}-scaling-dashboard')
  location: location
  kind: 'shared'
  properties: {
    displayName: '${resourcePrefix} Scaling Dashboard'
    serializedData: string(workbookDefinition)
    version: '1.0'
    sourceId: logAnalyticsWorkspaceId
    category: 'workbook'
  }
}

output workbookName string = workbook.name
output workbookResourceId string = workbook.id
