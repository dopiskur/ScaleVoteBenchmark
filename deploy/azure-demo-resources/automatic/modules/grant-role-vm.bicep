param vmName string
param principalId string

resource vmExisting 'Microsoft.Compute/virtualMachines@2024-07-01' existing = {
  name: vmName
}

resource role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vmExisting.id, principalId, 'Contributor')
  scope: vmExisting
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  }
}
