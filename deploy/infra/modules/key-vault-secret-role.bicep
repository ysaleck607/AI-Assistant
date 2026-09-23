targetScope = 'resourceGroup'

param keyVaultName string
param secretName string
param principalId string
param principalType string = 'ServicePrincipal'

@description('Key Vault Secrets User role definition id.')
param roleDefinitionId string = '4633458b-17de-408a-b874-0445c86b69e6'

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource secret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' existing = {
  parent: keyVault
  name: secretName
}

resource assignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(secret.id, principalId, roleDefinitionId)
  scope: secret
  properties: {
    principalId: principalId
    principalType: principalType
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleDefinitionId)
  }
}
