param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param vmResourceId string
param vmssResourceId string
param appServicePlanResourceId string
param webAppResourceId string
param sqlDatabaseResourceId string
param logAnalyticsWorkspaceId string

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

func azureMetricAvgQuery(resourceId string, metricName string) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Average) by bin(TimeGenerated, 1m) | render timechart'

func azureMetricTotalQuery(resourceId string, metricName string) string => 'AzureMetrics | where ResourceId =~ "${resourceId}" | where MetricName == "${metricName}" | summarize Value=avg(Total) by bin(TimeGenerated, 1m) | render timechart'

func insightsMetricQuery(resourceId string, namespaceName string, counterName string, perInstance bool) string => perInstance
  ? 'InsightsMetrics | where _ResourceId =~ "${resourceId}" | where Namespace == "${namespaceName}" and Name == "${counterName}" | summarize Value=avg(Val) by bin(TimeGenerated, 1m), Computer | render timechart'
  : 'InsightsMetrics | where _ResourceId =~ "${resourceId}" | where Namespace == "${namespaceName}" and Name == "${counterName}" | summarize Value=avg(Val) by bin(TimeGenerated, 1m) | render timechart'

var vmItems = [
  textItem('vm-header', '## VM (${resourcePrefix}-vm) - vertical scaling (Azure Monitor + Logic App resizes B1s -> B1ms)')
  kqlChartItem('vm-cpu', 'CPU %', insightsMetricQuery(vmResourceId, 'Processor', 'PercentProcessorTime', false), logAnalyticsWorkspaceId)
  kqlChartItem('vm-mem', 'Memory % used', insightsMetricQuery(vmResourceId, 'Memory', '% Used Memory', false), logAnalyticsWorkspaceId)
  kqlChartItem('vm-disk', 'Disk bytes/min (read+write)', azureMetricTotalQuery(vmResourceId, 'Disk Read Bytes'), logAnalyticsWorkspaceId)
  kqlChartItem('vm-net', 'Network In Total (bytes/min)', azureMetricTotalQuery(vmResourceId, 'Network In Total'), logAnalyticsWorkspaceId)
  textItem('vm-note', 'Single VM: no instance count tile - this scenario demonstrates vertical (resize), not horizontal, scaling.')
]

var vmssItems = [
  textItem('vmss-header', '## VM Scale Set (${resourcePrefix}-vmss) - horizontal scaling (native Autoscale)')
  argTableItem('vmss-instances', 'Current instance count', 'Resources | where id =~ "${vmssResourceId}" | project Name=name, InstanceCount=toint(sku.capacity)')
  kqlChartItem('vmss-cpu-avg', 'CPU % - average across all instances', azureMetricAvgQuery(vmssResourceId, 'Percentage CPU'), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-cpu-per-instance', 'CPU % - per instance', insightsMetricQuery(vmssResourceId, 'Processor', 'PercentProcessorTime', true), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-mem-avg', 'Memory % used - average across all instances', insightsMetricQuery(vmssResourceId, 'Memory', '% Used Memory', false), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-mem-per-instance', 'Memory % used - per instance', insightsMetricQuery(vmssResourceId, 'Memory', '% Used Memory', true), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-disk', 'Disk read bytes/min (aggregate)', azureMetricTotalQuery(vmssResourceId, 'Disk Read Bytes'), logAnalyticsWorkspaceId)
  kqlChartItem('vmss-net', 'Network In Total (bytes/min, aggregate)', azureMetricTotalQuery(vmssResourceId, 'Network In Total'), logAnalyticsWorkspaceId)
]

var servicePlanItems = [
  textItem('plan-header', '## App Service (${resourcePrefix}-webapp) - horizontal Autoscale + approval-gated vertical scaling')
  argTableItem('plan-instances', 'Current instance (worker) count', 'Resources | where id =~ "${appServicePlanResourceId}" | project Name=name, InstanceCount=toint(properties.numberOfWorkers)')
  kqlChartItem('plan-cpu', 'CPU %', azureMetricAvgQuery(appServicePlanResourceId, 'CpuPercentage'), logAnalyticsWorkspaceId)
  kqlChartItem('plan-mem', 'Memory %', azureMetricAvgQuery(appServicePlanResourceId, 'MemoryPercentage'), logAnalyticsWorkspaceId)
  kqlChartItem('plan-disk', 'Disk queue length', azureMetricAvgQuery(appServicePlanResourceId, 'DiskQueueLength'), logAnalyticsWorkspaceId)
  kqlChartItem('plan-net-in', 'Bytes received/min', azureMetricTotalQuery(webAppResourceId, 'BytesReceived'), logAnalyticsWorkspaceId)
  kqlChartItem('plan-net-out', 'Bytes sent/min', azureMetricTotalQuery(webAppResourceId, 'BytesSent'), logAnalyticsWorkspaceId)
]

var sqlItems = [
  textItem('sql-header', '## Azure SQL Database (${resourcePrefix}-sqldb) - Serverless, platform-native vertical scaling')
  kqlChartItem('sql-cpu', 'CPU %', azureMetricAvgQuery(sqlDatabaseResourceId, 'cpu_percent'), logAnalyticsWorkspaceId)
  kqlChartItem('sql-mem', 'App memory %', azureMetricAvgQuery(sqlDatabaseResourceId, 'app_memory_percent'), logAnalyticsWorkspaceId)
  kqlChartItem('sql-storage', 'Storage %', azureMetricAvgQuery(sqlDatabaseResourceId, 'storage_percent'), logAnalyticsWorkspaceId)
  textItem('sql-note', 'Serverless SQL DB has no "instance count" (it is a single logical database that scales vCores up/down, not out) and exposes no network metric - CPU is the signal to watch for the vertical-scaling behavior.')
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
