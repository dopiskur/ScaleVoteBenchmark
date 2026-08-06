param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param vmSize string = 'Standard_B1s'
param zone string = '3'
param adminUsername string

@secure()
param adminPassword string

param baseCapacity int = 1
param maxCapacity int = 4
param scaleOutThreshold int = 80
param scaleInThreshold int = 30
param cooldownMinutes int = 2
param logAnalyticsWorkspaceId string
param sqlResourceGroupName string
param addressPrefix string = '10.20.0.0/24'
param osDiskType string = 'Standard_LRS'
param imagePublisher string = 'Canonical'
param imageOffer string = 'ubuntu-24_04-lts'
param imageSku string = 'server'
param imageVersion string = 'latest'
param enableBootDiagnostics bool = true
param rollingUpgradeMaxBatchPercent int = 50
param rollingUpgradeMaxUnhealthyPercent int = 50
param rollingUpgradePauseBetweenBatches string = 'PT30S'
param retryMaxAttempts int = 5
param retryDelaySeconds int = 10

var vmssName = '${resourcePrefix}-vmss'
var loadBalancerName = '${resourcePrefix}-nlb'
var sqlResourceGroupId = '/subscriptions/${subscription().subscriptionId}/resourceGroups/${sqlResourceGroupName}'
var sqlServerName = toLower('${resourcePrefix}-sqlserver-${uniqueString(sqlResourceGroupId)}')
var sqlDatabaseName = '${resourcePrefix}-sqldb'
var sqlServerFqdn = '${sqlServerName}${environment().suffixes.sqlServerHostname}'

module network 'network-base.bicep' = {
  name: 'network-scale-set'
  params: {
    namePrefix: vmssName
    location: location
    addressPrefix: addressPrefix
    dnsLabelSeed: loadBalancerName
  }
}

module cloudInit 'cloud-init-scaletrigger.bicep' = {
  name: 'cloud-init-scale-set'
  params: {
    databaseProvider: 'MsSql'
    sqlServerFqdn: sqlServerFqdn
    sqlDatabaseName: sqlDatabaseName
    sqlAdminUsername: adminUsername
    sqlAdminPassword: adminPassword
    authUsername: adminUsername
    authPassword: adminPassword
    retryMaxAttempts: retryMaxAttempts
    retryDelaySeconds: retryDelaySeconds
  }
}

resource lb 'Microsoft.Network/loadBalancers@2023-11-01' = {
  name: loadBalancerName
  location: location
  sku: { name: 'Standard' }
  properties: {
    frontendIPConfigurations: [
      { name: 'LoadBalancerFrontEnd', properties: { publicIPAddress: { id: network.outputs.pipId } } }
    ]
    backendAddressPools: [ { name: 'backendPool' } ]
    probes: [
      {
        name: 'httpProbe'
        properties: { protocol: 'Tcp', port: 80, intervalInSeconds: 5, numberOfProbes: 2 }
      }
    ]
    loadBalancingRules: [
      {
        name: 'httpRule'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/loadBalancers/frontendIPConfigurations', loadBalancerName, 'LoadBalancerFrontEnd') }
          backendAddressPool: { id: resourceId('Microsoft.Network/loadBalancers/backendAddressPools', loadBalancerName, 'backendPool') }
          probe: { id: resourceId('Microsoft.Network/loadBalancers/probes', loadBalancerName, 'httpProbe') }
          protocol: 'Tcp'
          frontendPort: 80
          backendPort: 80
          idleTimeoutInMinutes: 4
        }
      }
      {
        name: 'httpsRule'
        properties: {
          frontendIPConfiguration: { id: resourceId('Microsoft.Network/loadBalancers/frontendIPConfigurations', loadBalancerName, 'LoadBalancerFrontEnd') }
          backendAddressPool: { id: resourceId('Microsoft.Network/loadBalancers/backendAddressPools', loadBalancerName, 'backendPool') }
          probe: { id: resourceId('Microsoft.Network/loadBalancers/probes', loadBalancerName, 'httpProbe') }
          protocol: 'Tcp'
          frontendPort: 443
          backendPort: 443
          idleTimeoutInMinutes: 4
        }
      }
    ]
  }
}

resource vmss 'Microsoft.Compute/virtualMachineScaleSets@2024-07-01' = {
  name: vmssName
  location: location
  zones: [zone]
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: vmSize
    tier: 'Standard'
    capacity: baseCapacity
  }
  properties: {
    overprovision: false
    upgradePolicy: {
      mode: 'Rolling'
      rollingUpgradePolicy: {
        maxBatchInstancePercent: rollingUpgradeMaxBatchPercent
        maxUnhealthyInstancePercent: rollingUpgradeMaxUnhealthyPercent
        maxUnhealthyUpgradedInstancePercent: rollingUpgradeMaxUnhealthyPercent
        pauseTimeBetweenBatches: rollingUpgradePauseBetweenBatches
      }
    }
    virtualMachineProfile: {
      osProfile: {
        computerNamePrefix: 'st'
        adminUsername: adminUsername
        adminPassword: adminPassword
        customData: cloudInit.outputs.cloudInitBase64
        linuxConfiguration: { disablePasswordAuthentication: false }
      }
      storageProfile: {
        imageReference: {
          publisher: imagePublisher
          offer: imageOffer
          sku: imageSku
          version: imageVersion
        }
        osDisk: {
          createOption: 'FromImage'
          managedDisk: { storageAccountType: osDiskType }
        }
      }
      networkProfile: {
        healthProbe: { id: resourceId('Microsoft.Network/loadBalancers/probes', loadBalancerName, 'httpProbe') }
        networkInterfaceConfigurations: [
          {
            name: '${vmssName}-nic'
            properties: {
              primary: true
              ipConfigurations: [
                {
                  name: 'ipconfig1'
                  properties: {
                    subnet: { id: network.outputs.subnetId }
                    loadBalancerBackendAddressPools: [
                      { id: resourceId('Microsoft.Network/loadBalancers/backendAddressPools', loadBalancerName, 'backendPool') }
                    ]
                  }
                }
              ]
            }
          }
        ]
      }
      diagnosticsProfile: {
        bootDiagnostics: {
          enabled: enableBootDiagnostics
        }
      }
      extensionProfile: {
        extensions: [
          {
            name: 'AzureMonitorLinuxAgent'
            properties: {
              publisher: 'Microsoft.Azure.Monitor'
              type: 'AzureMonitorLinuxAgent'
              typeHandlerVersion: '1.33'
              autoUpgradeMinorVersion: true
            }
          }
        ]
      }
    }
  }
  dependsOn: [ lb ]
}

resource autoscale 'Microsoft.Insights/autoscalesettings@2022-10-01' = {
  name: 'autoscale-${vmssName}'
  location: location
  properties: {
    enabled: true
    targetResourceUri: vmss.id
    profiles: [
      {
        name: 'default'
        capacity: { minimum: string(baseCapacity), maximum: string(maxCapacity), default: string(baseCapacity) }
        rules: [
          {
            metricTrigger: {
              metricName: 'Percentage CPU'
              metricResourceUri: vmss.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT1M'
              timeAggregation: 'Average'
              operator: 'GreaterThan'
              threshold: scaleOutThreshold
            }
            scaleAction: { direction: 'Increase', type: 'ChangeCount', value: '1', cooldown: 'PT${cooldownMinutes}M' }
          }
          {
            metricTrigger: {
              metricName: 'Percentage CPU'
              metricResourceUri: vmss.id
              timeGrain: 'PT1M'
              statistic: 'Average'
              timeWindow: 'PT1M'
              timeAggregation: 'Average'
              operator: 'LessThan'
              threshold: scaleInThreshold
            }
            scaleAction: { direction: 'Decrease', type: 'ChangeCount', value: '1', cooldown: 'PT${cooldownMinutes}M' }
          }
        ]
      }
    ]
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: vmss
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

resource autoscaleDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: autoscale
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logAnalyticsDestinationType: 'Dedicated'
    logs: [
      { category: 'AutoscaleEvaluations', enabled: true }
      { category: 'AutoscaleScaleActions', enabled: true }
    ]
  }
}

resource dcr 'Microsoft.Insights/dataCollectionRules@2023-03-11' = {
  name: '${vmssName}-dcr'
  location: location
  properties: {
    dataSources: {
      performanceCounters: [
        {
          name: 'guestCounters'
          streams: ['Microsoft-InsightsMetrics']
          samplingFrequencyInSeconds: 60
          counterSpecifiers: [
            '\\Processor\\PercentProcessorTime'
            '\\Memory\\% Used Memory'
          ]
        }
      ]
    }
    destinations: {
      logAnalytics: [
        {
          name: 'logAnalyticsDest'
          workspaceResourceId: logAnalyticsWorkspaceId
        }
      ]
    }
    dataFlows: [
      {
        streams: ['Microsoft-InsightsMetrics']
        destinations: ['logAnalyticsDest']
      }
    ]
  }
}

resource dcrAssociation 'Microsoft.Insights/dataCollectionRuleAssociations@2023-03-11' = {
  name: 'dcr-association'
  scope: vmss
  properties: {
    dataCollectionRuleId: dcr.id
  }
}

output vmssResourceId string = vmss.id
output vmssFqdn string = network.outputs.pipFqdn
output loadBalancerName string = lb.name
