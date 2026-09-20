targetScope = 'subscription'

@description('Name of the existing Azure Container Registry.')
param acrName string

@description('Resource group containing the existing Azure Container Registry.')
param sharedResourceGroupName string

@description('Principal ID of the user-assigned identity used to pull images.')
param principalId string

module acrPullRole './acr-pull-role.bicep' = {
  name: 'acr-pull-role'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    acrName: acrName
    principalId: principalId
  }
}
