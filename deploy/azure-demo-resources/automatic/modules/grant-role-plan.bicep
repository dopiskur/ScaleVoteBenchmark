param planName string
param principalId string

resource planExisting 'Microsoft.Web/serverfarms@2023-12-01' existing = {
  name: planName
}

resource role 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(planExisting.id, principalId, 'Contributor')
  scope: planExisting
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'b24988ac-6180-42a0-ab88-20f7382dd24c')
  }
}
