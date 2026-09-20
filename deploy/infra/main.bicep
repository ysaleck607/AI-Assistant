targetScope = 'resourceGroup'

param location string = resourceGroup().location

@allowed([
  'certif'
  'prod'
])
param environmentName string

@minLength(3)
@maxLength(8)
param nameSuffix string

@description('Resource group containing the shared ACR.')
param sharedResourceGroupName string

@description('Immutable backend image tag, normally sha-<Git commit SHA>.')
param backendImageTag string

@description('Immutable SPA image tag, normally sha-<Git commit SHA>.')
param spaImageTag string

param sqlAdministratorLogin string = 'assistantadmin'
param sqlDatabaseName string = environmentName == 'certif' ? 'AssistantCoreDbCertif' : 'AssistantCoreDb'
param azureAdTenantId string = 'organizations'
param azureAdApiClientId string = environmentName == 'prod'
  ? '50058836-543d-4def-be0a-d4d5290d29ad'
  : 'f70fb50b-52d5-4346-b769-1121cb3ab3e2'

@description('Confidential Entra application used by the BFF. Do not reuse a public SPA registration.')
param bffEntraClientId string

param microsoft365ClientId string = environmentName == 'prod'
  ? 'f2907152-55be-49ef-b230-9b6821478433'
  : '558d6670-3549-423e-ae92-c5ff1d3b326b'

param dataProtectionKeysStorageAccountName string = environmentName == 'prod'
  ? 'assistantprodkeysonp01'
  : 'assistantcertkeys01'
param dataProtectionKeyName string = 'dataprotection-key'
param azureSearchEndpoint string = 'https://srch-assistant-onp01.search.windows.net'
param azureSearchIndexName string = environmentName == 'prod'
  ? 'ogcomptabilite-index'
  : 'microsoft-content-certif'
param azureSearchKnowledgeSourceName string = environmentName == 'prod'
  ? 'ogcomptabilite-knowledge-source'
  : 'synaptix-m365-certif-knowledge-source'
param azureSearchKnowledgeBaseName string = environmentName == 'prod'
  ? 'ogcomptabilite-knowledge-base'
  : 'synaptix-m365-certif-knowledge-base'
param azureOpenAiEmbeddingEndpoint string = environmentName == 'prod'
  ? 'https://aoai-assistant-prod-onp01.openai.azure.com'
  : 'https://onpremia-openai-search.openai.azure.com'
param azureOpenAiEmbeddingDeploymentName string = 'm365-text-embedding-3-small'
param azureOpenAiEmbeddingModelName string = 'text-embedding-3-small'
param azureOpenAiPlanningEndpoint string = environmentName == 'prod'
  ? 'https://ai-assistant-prod-onp01.openai.azure.com'
  : 'https://josetchibozo7-5469-resource.openai.azure.com'
param azureOpenAiPlanningDeploymentName string = environmentName == 'prod' ? 'gpt-5.5-prod' : 'gpt-5.5-1'
param azureOpenAiPlanningModelName string = 'gpt-5.5'
param foundryAccountName string = environmentName == 'prod' ? 'ai-assistant-prod-onp01' : 'josetchibozo7-5469-resource'
param foundryProjectName string = environmentName == 'prod' ? 'onpremia-production' : 'onpremia-openai-search'
param foundryAgentName string = environmentName == 'prod' ? 'onpremia-production-agent' : 'onpremia-test-agent'
param foundryAgentVersion string = environmentName == 'prod' ? '1' : '17'
param azureVisionOcrEndpoint string = environmentName == 'prod'
  ? 'https://vision-assistant-prod-onp01.cognitiveservices.azure.com/'
  : 'https://vision-assistant.cognitiveservices.azure.com/'

@allowed([
  'minimal'
  'low'
  'auto'
])
param knowledgeBaseRetrievalReasoningEffort string = 'auto'

param tags object = {
  application: 'assistant'
  environment: environmentName
  managedBy: 'bicep'
}

var acrName = 'acrassistant${nameSuffix}'
var acrLoginServer = '${acrName}.azurecr.io'
var keyVaultEnvironmentName = environmentName == 'certif' ? 'cert' : environmentName
var keyVaultName = 'kv-assistant-${keyVaultEnvironmentName}-${nameSuffix}'
var runtimeEnvironmentName = environmentName == 'prod' ? 'Production' : 'Certif'
var spaLaunchMode = environmentName == 'prod' ? 'Production' : 'Certification'
var containerEnvironmentName = 'cae-assistant-${environmentName}'
var apiAppName = 'ca-assistant-api-${environmentName}'
var workerAppName = 'ca-assistant-worker-${environmentName}'
var spaAppName = 'ca-assistant-spa-${environmentName}'
var migrationsJobName = 'caj-assistant-migrations-${environmentName}'
var apiIdentityName = 'id-assistant-api-${environmentName}'
var workerIdentityName = 'id-assistant-worker-${environmentName}'
var bffIdentityName = 'id-assistant-bff-${environmentName}'
var migrationsIdentityName = 'id-assistant-migrations-${environmentName}'
var acrPullIdentityName = 'id-assistant-acr-${environmentName}'
var dataProtectionKeysContainerName = 'dataprotection-keys'
var publicDomain = 'assistant-${environmentName}.onpremia.ca'
var publicOrigin = 'https://${publicDomain}'
var foundryProjectEndpoint = 'https://${foundryAccountName}.services.ai.azure.com/api/projects/${foundryProjectName}'
var keyVaultBaseUrl = 'https://${keyVaultName}.${environment().suffixes.keyvaultDns}/secrets'
var dataProtectionBlobUri = '${dataProtectionKeysStorage.properties.primaryEndpoints.blob}${dataProtectionKeysContainerName}/keys.xml'
var bffDataProtectionBlobUri = '${dataProtectionKeysStorage.properties.primaryEndpoints.blob}${dataProtectionKeysContainerName}/bff-keys.xml'

var runtimeSecretNames = [
  'microsoft365-client-secret'
  'microsoft365-clientstate-hmac-key'
  'microsoft365-sharepoint-certificate-pfx'
  'microsoft365-sharepoint-certificate-password'
  'azure-vision-ocr-api-key'
  'azure-openai-embedding-api-key'
  'azure-openai-planning-api-key'
  'azure-search-api-key'
  'member-pii-email-lookup-hmac-key'
]

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource dataProtectionKeysStorage 'Microsoft.Storage/storageAccounts@2023-05-01' existing = {
  name: dataProtectionKeysStorageAccountName
}

resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' existing = {
  parent: keyVault
  name: dataProtectionKeyName
}

resource foundryAccount 'Microsoft.CognitiveServices/accounts@2025-06-01' existing = {
  name: foundryAccountName
}

resource foundryProject 'Microsoft.CognitiveServices/accounts/projects@2025-06-01' existing = {
  parent: foundryAccount
  name: foundryProjectName
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: apiIdentityName
  location: location
  tags: tags
}

resource workerIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: workerIdentityName
  location: location
  tags: tags
}

resource bffIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: bffIdentityName
  location: location
  tags: tags
}

resource migrationsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: migrationsIdentityName
  location: location
  tags: tags
}

resource acrPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: acrPullIdentityName
  location: location
  tags: tags
}

module acrPullRole './modules/acr-pull-role-subscription.bicep' = {
  name: 'acr-pull-${environmentName}'
  scope: subscription()
  params: {
    acrName: acrName
    sharedResourceGroupName: sharedResourceGroupName
    principalId: acrPullIdentity.properties.principalId
  }
}

module apiSecretRoles './modules/key-vault-secret-role.bicep' = [for secretName in runtimeSecretNames: {
  name: 'api-kv-${uniqueString(secretName)}'
  params: {
    keyVaultName: keyVault.name
    secretName: secretName
    principalId: apiIdentity.properties.principalId
  }
}]

module workerSecretRoles './modules/key-vault-secret-role.bicep' = [for secretName in runtimeSecretNames: {
  name: 'worker-kv-${uniqueString(secretName)}'
  params: {
    keyVaultName: keyVault.name
    secretName: secretName
    principalId: workerIdentity.properties.principalId
  }
}]

module bffSecretRole './modules/key-vault-secret-role.bicep' = {
  name: 'bff-kv-client-secret'
  params: {
    keyVaultName: keyVault.name
    secretName: 'bff-entra-client-secret'
    principalId: bffIdentity.properties.principalId
  }
}

module migrationsSecretRole './modules/key-vault-secret-role.bicep' = {
  name: 'migrations-kv-sql-admin'
  params: {
    keyVaultName: keyVault.name
    secretName: 'sql-admin-password'
    principalId: migrationsIdentity.properties.principalId
  }
}

resource apiDataProtectionBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKeysStorage.id, apiIdentity.id, 'StorageBlobDataContributor')
  scope: dataProtectionKeysStorage
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource workerDataProtectionBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKeysStorage.id, workerIdentity.id, 'StorageBlobDataContributor')
  scope: dataProtectionKeysStorage
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource bffDataProtectionBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKeysStorage.id, bffIdentity.id, 'StorageBlobDataContributor')
  scope: dataProtectionKeysStorage
  properties: {
    principalId: bffIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe')
  }
}

resource apiDataProtectionKeyRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, apiIdentity.id, 'KeyVaultCryptoUser')
  scope: dataProtectionKey
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bde6-1746d40b5a80')
  }
}

resource workerDataProtectionKeyRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, workerIdentity.id, 'KeyVaultCryptoUser')
  scope: dataProtectionKey
  properties: {
    principalId: workerIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bde6-1746d40b5a80')
  }
}

resource bffDataProtectionKeyRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, bffIdentity.id, 'KeyVaultCryptoUser')
  scope: dataProtectionKey
  properties: {
    principalId: bffIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '12338af0-0e69-4776-bde6-1746d40b5a80')
  }
}

resource apiFoundryRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryProject.id, apiIdentity.id, 'FoundryUser')
  scope: foundryProject
  properties: {
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '53ca6127-db72-4b80-b1b0-d745d6d5456d')
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql-${environmentName}'
  params: {
    location: location
    environmentName: environmentName
    nameSuffix: nameSuffix
    administratorLogin: sqlAdministratorLogin
    databaseName: sqlDatabaseName
    administratorPassword: keyVault.getSecret('sql-admin-password')
    publicNetworkAccess: 'Disabled'
    productionWorkload: environmentName == 'prod'
    tags: tags
  }
}

module privateNetwork './modules/private-network.bicep' = {
  name: 'private-network-${environmentName}'
  params: {
    location: location
    environmentName: environmentName
    nameSuffix: nameSuffix
    sqlServerId: sql.outputs.serverId
    sqlServerName: sql.outputs.serverName
    tags: tags
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2026-01-01' = {
  name: containerEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    publicNetworkAccess: 'Disabled'
    vnetConfiguration: {
      infrastructureSubnetId: privateNetwork.outputs.containerAppsSubnetId
      internal: true
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: environmentName == 'prod'
  }
}

var registryConfiguration = [
  {
    server: acrLoginServer
    identity: acrPullIdentity.id
  }
]

var apiSecrets = [for secretName in runtimeSecretNames: {
  name: secretName
  keyVaultUrl: '${keyVaultBaseUrl}/${secretName}'
  identity: apiIdentity.id
}]

var workerSecrets = [for secretName in runtimeSecretNames: {
  name: secretName
  keyVaultUrl: '${keyVaultBaseUrl}/${secretName}'
  identity: workerIdentity.id
}]

var apiDatabaseConnection = 'Server=tcp:${sql.outputs.serverFqdn},1433;Initial Catalog=${sql.outputs.databaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;User Id=${apiIdentity.properties.clientId};'
var workerDatabaseConnection = 'Server=tcp:${sql.outputs.serverFqdn},1433;Initial Catalog=${sql.outputs.databaseName};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity;User Id=${workerIdentity.properties.clientId};'

var commonRuntimeEnvironmentVariables = [
  {
    name: 'Microsoft365__ConsentCallbackUrl'
    value: '${publicOrigin}/api/microsoft365/consent/callback'
  }
  {
    name: 'Microsoft365__ConsentSuccessRedirectUrl'
    value: '${publicOrigin}/microsoft365/consent/success'
  }
  {
    name: 'Microsoft365__ConsentErrorRedirectUrl'
    value: '${publicOrigin}/microsoft365/consent/error'
  }
  {
    name: 'Microsoft365__WebhookBaseUrl'
    value: publicOrigin
  }
  {
    name: 'DataProtectionKeyStorage__BlobStorageUri'
    value: dataProtectionBlobUri
  }
  {
    name: 'DataProtectionKeyStorage__KeyVaultKeyUri'
    value: dataProtectionKey.properties.keyUriWithVersion
  }
  {
    name: 'Microsoft365__ClientId'
    value: microsoft365ClientId
  }
  {
    name: 'Microsoft365__ClientSecret'
    secretRef: 'microsoft365-client-secret'
  }
  {
    name: 'Microsoft365__ClientStateHmacKey'
    secretRef: 'microsoft365-clientstate-hmac-key'
  }
  {
    name: 'MemberPii__EmailLookupHmacKey'
    secretRef: 'member-pii-email-lookup-hmac-key'
  }
  {
    name: 'Microsoft365__SharePointCertificateBase64'
    secretRef: 'microsoft365-sharepoint-certificate-pfx'
  }
  {
    name: 'Microsoft365__SharePointCertificatePassword'
    secretRef: 'microsoft365-sharepoint-certificate-password'
  }
  {
    name: 'Microsoft365__OcrApiKey'
    secretRef: 'azure-vision-ocr-api-key'
  }
  {
    name: 'Microsoft365__OcrEndpoint'
    value: azureVisionOcrEndpoint
  }
  {
    name: 'Microsoft365__EmbeddingApiKey'
    secretRef: 'azure-openai-embedding-api-key'
  }
  {
    name: 'Microsoft365__EmbeddingEndpoint'
    value: azureOpenAiEmbeddingEndpoint
  }
  {
    name: 'Microsoft365__EmbeddingDeploymentName'
    value: azureOpenAiEmbeddingDeploymentName
  }
  {
    name: 'Microsoft365__EmbeddingModel'
    value: azureOpenAiEmbeddingModelName
  }
  {
    name: 'AzureSearch__Endpoint'
    value: azureSearchEndpoint
  }
  {
    name: 'AzureSearch__IndexName'
    value: azureSearchIndexName
  }
  {
    name: 'AzureSearch__KnowledgeSourceName'
    value: azureSearchKnowledgeSourceName
  }
  {
    name: 'AzureSearch__KnowledgeBaseName'
    value: azureSearchKnowledgeBaseName
  }
  {
    name: 'AzureSearch__ApiKey'
    secretRef: 'azure-search-api-key'
  }
  {
    name: 'AzureSearch__KnowledgeBaseRetrievalReasoningEffort'
    value: knowledgeBaseRetrievalReasoningEffort
  }
  {
    name: 'AzureSearch__PlanningModelEndpoint'
    value: azureOpenAiPlanningEndpoint
  }
  {
    name: 'AzureSearch__PlanningModelDeploymentName'
    value: azureOpenAiPlanningDeploymentName
  }
  {
    name: 'AzureSearch__PlanningModelName'
    value: azureOpenAiPlanningModelName
  }
  {
    name: 'AzureSearch__PlanningModelApiKey'
    secretRef: 'azure-openai-planning-api-key'
  }
  {
    name: 'FoundryAgent__ProjectEndpoint'
    value: foundryProjectEndpoint
  }
  {
    name: 'FoundryAgent__AgentName'
    value: foundryAgentName
  }
  {
    name: 'FoundryAgent__AgentVersion'
    value: foundryAgentVersion
  }
]

resource api 'Microsoft.App/containerApps@2025-01-01' = {
  name: apiAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${apiIdentity.id}': {}
      '${acrPullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfiguration
      secrets: apiSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/assistant-api:${backendImageTag}'
          env: concat([
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: runtimeEnvironmentName
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: apiIdentity.properties.clientId
            }
            {
              name: 'ConnectionStrings__AssistantCoreDatabase'
              value: apiDatabaseConnection
            }
            {
              name: 'Cors__AllowedOrigins__0'
              value: publicOrigin
            }
            {
              name: 'AzureAd__TenantId'
              value: azureAdTenantId
            }
            {
              name: 'AzureAd__ClientId'
              value: azureAdApiClientId
            }
            {
              name: 'AzureAd__Audience'
              value: azureAdApiClientId
            }
          ], commonRuntimeEnvironmentVariables)
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              periodSeconds: 5
              failureThreshold: 24
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 15
            }
          ]
        }
      ]
      scale: {
        minReplicas: environmentName == 'prod' ? 2 : 1
        maxReplicas: environmentName == 'prod' ? 5 : 1
      }
    }
  }
  dependsOn: [
    acrPullRole
    apiSecretRoles
    apiDataProtectionBlobRole
    apiDataProtectionKeyRole
    apiFoundryRole
  ]
}

resource worker 'Microsoft.App/containerApps@2025-01-01' = {
  name: workerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${workerIdentity.id}': {}
      '${acrPullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      registries: registryConfiguration
      secrets: workerSecrets
    }
    template: {
      containers: [
        {
          name: 'worker'
          image: '${acrLoginServer}/assistant-worker:${backendImageTag}'
          env: concat([
            {
              name: 'DOTNET_ENVIRONMENT'
              value: runtimeEnvironmentName
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: workerIdentity.properties.clientId
            }
            {
              name: 'ConnectionStrings__AssistantCoreDatabase'
              value: workerDatabaseConnection
            }
          ], commonRuntimeEnvironmentVariables)
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: environmentName == 'prod' ? 3 : 1
      }
    }
  }
  dependsOn: [
    acrPullRole
    workerSecretRoles
    workerDataProtectionBlobRole
    workerDataProtectionKeyRole
  ]
}

var internalApiBaseUrl = 'https://${api.properties.configuration.ingress.fqdn}'

resource spa 'Microsoft.App/containerApps@2025-01-01' = {
  name: spaAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${bffIdentity.id}': {}
      '${acrPullIdentity.id}': {}
    }
  }
  properties: {
    managedEnvironmentId: containerEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        allowInsecure: false
        targetPort: 8080
        transport: 'auto'
      }
      registries: registryConfiguration
      secrets: [
        {
          name: 'bff-entra-client-secret'
          keyVaultUrl: '${keyVaultBaseUrl}/bff-entra-client-secret'
          identity: bffIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'spa'
          image: '${acrLoginServer}/assistant-spa:${spaImageTag}'
          env: [
            {
              name: 'SPA_API_BASE_URL'
              value: '/'
            }
            {
              name: 'SPA_AUTHENTICATION_MODE'
              value: 'Bff'
            }
            {
              name: 'SPA_LAUNCH_MODE'
              value: spaLaunchMode
            }
            {
              name: 'SPA_AUTHENTICATION_URL'
              value: '/bff'
            }
            {
              name: 'SPA_BFF_UPSTREAM'
              value: 'http://127.0.0.1:8081'
            }
            {
              name: 'SPA_API_UPSTREAM'
              value: internalApiBaseUrl
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
        {
          name: 'bff'
          image: '${acrLoginServer}/assistant-bff:${backendImageTag}'
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: runtimeEnvironmentName
            }
            {
              name: 'ASPNETCORE_URLS'
              value: 'http://+:8081'
            }
            {
              name: 'AZURE_CLIENT_ID'
              value: bffIdentity.properties.clientId
            }
            {
              name: 'AzureAd__Instance'
              value: environment().authentication.loginEndpoint
            }
            {
              name: 'AzureAd__TenantId'
              value: azureAdTenantId
            }
            {
              name: 'AzureAd__ClientId'
              value: bffEntraClientId
            }
            {
              name: 'AzureAd__ClientSecret'
              secretRef: 'bff-entra-client-secret'
            }
            {
              name: 'AzureAd__CallbackPath'
              value: '/signin-oidc'
            }
            {
              name: 'AzureAd__SignedOutCallbackPath'
              value: '/signout-callback-oidc'
            }
            {
              name: 'Bff__PublicOrigin'
              value: publicOrigin
            }
            {
              name: 'DataProtectionKeyStorage__BlobStorageUri'
              value: bffDataProtectionBlobUri
            }
            {
              name: 'DataProtectionKeyStorage__KeyVaultKeyUri'
              value: dataProtectionKey.properties.keyUriWithVersion
            }
            {
              name: 'DownstreamApi__BaseUrl'
              value: internalApiBaseUrl
            }
            {
              name: 'DownstreamApi__Scopes__0'
              value: 'api://${azureAdApiClientId}/access_as_user'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          probes: [
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: 8081
                scheme: 'HTTP'
              }
              periodSeconds: 5
              failureThreshold: 24
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8081
                scheme: 'HTTP'
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
          ]
        }
      ]
      scale: {
        minReplicas: environmentName == 'prod' ? 2 : 1
        maxReplicas: environmentName == 'prod' ? 5 : 1
      }
    }
  }
  dependsOn: [
    acrPullRole
    bffSecretRole
    bffDataProtectionBlobRole
    bffDataProtectionKeyRole
  ]
}

resource migrationsJob 'Microsoft.App/jobs@2025-01-01' = {
  name: migrationsJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationsIdentity.id}': {}
      '${acrPullIdentity.id}': {}
    }
  }
  properties: {
    environmentId: containerEnvironment.id
    configuration: {
      triggerType: 'Manual'
      replicaTimeout: 1800
      replicaRetryLimit: 0
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: registryConfiguration
      secrets: [
        {
          name: 'sql-admin-password'
          keyVaultUrl: '${keyVaultBaseUrl}/sql-admin-password'
          identity: migrationsIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrations'
          image: '${acrLoginServer}/assistant-migrations:${backendImageTag}'
          args: [
            'migrate'
          ]
          env: [
            {
              name: 'FLYWAY_URL'
              value: 'jdbc:sqlserver://${sql.outputs.serverFqdn}:1433;databaseName=${sql.outputs.databaseName};encrypt=true;trustServerCertificate=false;hostNameInCertificate=*.database.windows.net;loginTimeout=30'
            }
            {
              name: 'FLYWAY_USER'
              value: sqlAdministratorLogin
            }
            {
              name: 'FLYWAY_PASSWORD'
              secretRef: 'sql-admin-password'
            }
            {
              name: 'FLYWAY_CONNECT_RETRIES'
              value: '60'
            }
            {
              name: 'FLYWAY_BASELINE_ON_MIGRATE'
              value: 'false'
            }
            {
              name: 'FLYWAY_PLACEHOLDERS_API_IDENTITY_NAME'
              value: apiIdentity.name
            }
            {
              name: 'FLYWAY_PLACEHOLDERS_API_CLIENT_ID'
              value: apiIdentity.properties.clientId
            }
            {
              name: 'FLYWAY_PLACEHOLDERS_WORKER_IDENTITY_NAME'
              value: workerIdentity.name
            }
            {
              name: 'FLYWAY_PLACEHOLDERS_WORKER_CLIENT_ID'
              value: workerIdentity.properties.clientId
            }
          ]
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
    }
  }
  dependsOn: [
    acrPullRole
    migrationsSecretRole
  ]
}

module frontDoor './modules/front-door.bicep' = {
  name: 'front-door-${environmentName}'
  params: {
    location: location
    environmentName: environmentName
    nameSuffix: nameSuffix
    originHostName: spa.properties.configuration.ingress.fqdn
    privateLinkResourceId: containerEnvironment.id
    customDomainName: publicDomain
    tags: tags
  }
}

output apiName string = api.name
output apiInternalUrl string = internalApiBaseUrl
output spaName string = spa.name
output publicUrl string = publicOrigin
output workerName string = worker.name
output migrationsJobName string = migrationsJob.name
output keyVaultName string = keyVault.name
output sqlServerName string = sql.outputs.serverName
output sqlDatabaseName string = sql.outputs.databaseName
output frontDoorProfileName string = frontDoor.outputs.profileName
output frontDoorEndpointHostName string = frontDoor.outputs.endpointHostName
