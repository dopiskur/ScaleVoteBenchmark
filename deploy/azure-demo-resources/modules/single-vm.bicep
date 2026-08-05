param resourcePrefix string = 'ScaleTrigger'
param location string = resourceGroup().location
param vmSize string = 'Standard_B1s'
param zone string = '3'
param adminUsername string

@secure()
param adminPassword string

param logAnalyticsWorkspaceId string
param addressPrefix string = '10.10.0.0/24'
param osDiskType string = 'Standard_LRS'
param imagePublisher string = 'Canonical'
param imageOffer string = 'ubuntu-24_04-lts'
param imageSku string = 'server'
param imageVersion string = 'latest'
param enableBootDiagnostics bool = true
param enableAutoShutdown bool = true
param autoShutdownTime string = '0500'
param autoShutdownTimeZone string = 'Central European Standard Time'
param retryMaxAttempts int = 5
param retryDelaySeconds int = 10

var vmName = '${resourcePrefix}-vm'

module network 'network-base.bicep' = {
  name: 'network-single-vm'
  params: {
    namePrefix: vmName
    location: location
    addressPrefix: addressPrefix
  }
}

module cloudInit 'cloud-init-scaletrigger.bicep' = {
  name: 'cloud-init-single-vm'
  params: {
    databaseProvider: 'Sqlite'
    authUsername: adminUsername
    authPassword: adminPassword
    retryMaxAttempts: retryMaxAttempts
    retryDelaySeconds: retryDelaySeconds
  }
}

resource nic 'Microsoft.Network/networkInterfaces@2023-11-01' = {
  name: '${vmName}-nic'
  location: location
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          subnet: { id: network.outputs.subnetId }
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: { id: network.outputs.pipId }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: vmName
  location: location
  zones: [zone]
  properties: {
    hardwareProfile: { vmSize: vmSize }
    osProfile: {
      computerName: vmName
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
      networkInterfaces: [{ id: nic.id }]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: enableBootDiagnostics
      }
    }
  }
}

resource autoShutdown 'Microsoft.DevTestLab/schedules@2018-09-15' = if (enableAutoShutdown) {
  name: 'shutdown-computevm-${vmName}'
  location: location
  properties: {
    status: 'Enabled'
    taskType: 'ComputeVmShutdownTask'
    dailyRecurrence: { time: autoShutdownTime }
    timeZoneId: autoShutdownTimeZone
    targetResourceId: vm.id
    notificationSettings: { status: 'Disabled' }
  }
}

resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'send-to-log-analytics'
  scope: vm
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output vmResourceId string = vm.id
output vmName string = vm.name
output vmFqdn string = network.outputs.pipFqdn
