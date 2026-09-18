targetScope = 'resourceGroup'

param location string = resourceGroup().location

@description('Compte de stockage qui heberge le trousseau de cles Data Protection.')
param dataProtectionKeysStorageAccountName string = 'assistantcertkeys01'

@description('Cle Key Vault qui chiffre le trousseau Data Protection.')
param dataProtectionKeyName string = 'dataprotection-key'

@allowed([
  'certif'
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

param azureAdTenantId string = 'organizations'
param azureAdApiClientId string = 'f70fb50b-52d5-4346-b769-1121cb3ab3e2'
param microsoft365ClientId string = environmentName == 'certif'
  ? '558d6670-3549-423e-ae92-c5ff1d3b326b'
  : '00000000-0000-0000-0000-000000000001'
param spaEntraClientId string = '97fda345-b54e-4243-b05a-31623871df18'
param azureSearchEndpoint string = 'https://synaptixsearch.search.windows.net'
param azureSearchIndexName string = 'microsoft-content-${environmentName}'
param azureOpenAiEmbeddingEndpoint string = 'https://onpremia-openai-search.openai.azure.com'
param azureOpenAiEmbeddingDeploymentName string = 'm365-text-embedding-3-small'
param azureOpenAiEmbeddingModelName string = 'text-embedding-3-small'
param azureOpenAiPlanningEndpoint string = 'https://josetchibozo7-5469-resource.openai.azure.com'
param azureOpenAiPlanningDeploymentName string = 'gpt-5.5-1'
param azureOpenAiPlanningModelName string = 'gpt-5.5'
param foundryAccountName string = 'josetchibozo7-5469-resource'
param foundryProjectName string = 'onpremia-openai-search'
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
var keyVaultEnvironmentName = 'cert'
var keyVaultName = 'kv-assistant-${keyVaultEnvironmentName}-${nameSuffix}'
var containerEnvironmentName = 'cae-assistant-${environmentName}'
var apiAppName = 'ca-assistant-api-${environmentName}'
var workerAppName = 'ca-assistant-worker-${environmentName}'
var spaAppName = 'ca-assistant-spa-${environmentName}'
var certifSpaCustomDomain = 'assistant-certif.onpremia.ca'
var certifBffCustomDomain = 'assistant-bff-certif.onpremia.ca'
var certifSpaCertificateName = 'assistant-certif.onpremia.ca-cae-assi-260904041349'
var certifBffCertificateName = 'assistant-bff-certif.onpremi-cae-assi-260904035728'
var migrationsJobName = 'caj-assistant-migrations-${environmentName}'
var workloadIdentityName = 'id-assistant-workload-${environmentName}'
var acrPullIdentityName = 'id-assistant-acr-${environmentName}'
var dataProtectionKeysContainerName = 'dataprotection-keys'

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

resource workloadIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: workloadIdentityName
  location: location
  tags: tags
}

resource acrPullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: acrPullIdentityName
  location: location
  tags: tags
}

resource keyVaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, workloadIdentity.id, 'KeyVaultSecretsUser')
  scope: keyVault
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '4633458b-17de-408a-b874-0445c86b69e6'
    )
  }
}

resource dataProtectionKeysBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKeysStorage.id, workloadIdentity.id, 'StorageBlobDataContributor')
  scope: dataProtectionKeysStorage
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
    )
  }
}

resource dataProtectionKeyCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, workloadIdentity.id, 'KeyVaultCryptoUser')
  scope: dataProtectionKey
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '12338af0-0e69-4776-bde6-1746d40b5a80'
    )
  }
}

resource foundryUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(foundryProject.id, workloadIdentity.id, 'FoundryUser')
  scope: foundryProject
  properties: {
    principalId: workloadIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions',
      '53ca6127-db72-4b80-b1b0-d745d6d5456d'
    )
  }
}

module acrPullRole './modules/acr-pull-role.bicep' = {
  name: 'acr-pull-${environmentName}'
  scope: resourceGroup(sharedResourceGroupName)
  params: {
    acrName: acrName
    principalId: acrPullIdentity.properties.principalId
  }
}

module sql './modules/sql.bicep' = {
  name: 'sql-${environmentName}'
  params: {
    location: location
    environmentName: environmentName
    nameSuffix: nameSuffix
    keyVaultName: keyVault.name
    administratorLogin: sqlAdministratorLogin
    administratorPassword: keyVault.getSecret('sql-admin-password')
    tags: tags
  }
}

resource containerEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: containerEnvironmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    zoneRedundant: false
  }
}

var apiBaseUrl = 'https://${certifBffCustomDomain}'
var spaBaseUrl = 'https://${certifSpaCustomDomain}'
var keyVaultBaseUrl = '${keyVault.properties.vaultUri}secrets'

var certifSpaCertificateId = resourceId(
  'Microsoft.App/managedEnvironments/managedCertificates',
  containerEnvironmentName,
  certifSpaCertificateName
)
var certifBffCertificateId = resourceId(
  'Microsoft.App/managedEnvironments/managedCertificates',
  containerEnvironmentName,
  certifBffCertificateName
)

var managedIdentities = {
  '${workloadIdentity.id}': {}
  '${acrPullIdentity.id}': {}
}

var registryConfiguration = [
  {
    server: acrLoginServer
    identity: acrPullIdentity.id
  }
]

var databaseSecret = {
  name: 'database-connection'
  keyVaultUrl: sql.outputs.connectionSecretUri
  identity: workloadIdentity.id
}

var apiSecrets = [
  databaseSecret
      {
        name: 'microsoft365-client-secret'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-client-secret'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-clientstate-hmac-key'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-clientstate-hmac-key'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-sharepoint-certificate-pfx'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-sharepoint-certificate-pfx'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-sharepoint-certificate-password'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-sharepoint-certificate-password'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-vision-ocr-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-vision-ocr-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-openai-embedding-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-openai-embedding-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-openai-planning-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-openai-planning-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-search-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-search-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'member-pii-email-lookup-hmac-key'
        keyVaultUrl: '${keyVaultBaseUrl}/member-pii-email-lookup-hmac-key'
        identity: workloadIdentity.id
      }
    ]

var commonApiEnvironmentVariables = [
  {
    name: 'ASPNETCORE_ENVIRONMENT'
    value: 'Certif'
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: workloadIdentity.properties.clientId
  }
  {
    name: 'ConnectionStrings__AssistantCoreDatabase'
    secretRef: 'database-connection'
  }
  {
    name: 'Cors__AllowedOrigins__0'
    value: spaBaseUrl
  }
  {
    name: 'Microsoft365__ConsentCallbackUrl'
    value: '${apiBaseUrl}/api/microsoft365/consent/callback'
  }
  {
    name: 'Microsoft365__ConsentSuccessRedirectUrl'
    value: '${spaBaseUrl}/microsoft365/consent/success'
  }
  {
    name: 'Microsoft365__ConsentErrorRedirectUrl'
    value: '${spaBaseUrl}/microsoft365/consent/error'
  }
  {
    name: 'Microsoft365__WebhookBaseUrl'
    value: apiBaseUrl
  }
  {
    name: 'DataProtectionKeyStorage__BlobStorageUri'
    value: '${dataProtectionKeysStorage.properties.primaryEndpoints.blob}${dataProtectionKeysContainerName}/keys.xml'
  }
  {
    name: 'DataProtectionKeyStorage__KeyVaultKeyUri'
    value: dataProtectionKey.properties.keyUriWithVersion
  }
]

var certifApiEnvironmentVariables = [
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
    value: 'https://vision-assistant.cognitiveservices.azure.com/'
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
]

resource api 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: managedIdentities
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
        customDomains: [
          {
            name: certifBffCustomDomain
            certificateId: certifBffCertificateId
            bindingType: 'SniEnabled'
          }
        ]
      }
      registries: registryConfiguration
      secrets: apiSecrets
    }
    template: {
      containers: [
        {
          name: 'api'
          image: '${acrLoginServer}/assistant-api:${backendImageTag}'
          env: concat(commonApiEnvironmentVariables, certifApiEnvironmentVariables)
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
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [acrPullRole, keyVaultSecretsUser, foundryUser, dataProtectionKeysBlobContributor, dataProtectionKeyCryptoUser]
}

var workerSecrets = [
  databaseSecret
      {
        name: 'microsoft365-client-secret'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-client-secret'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-clientstate-hmac-key'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-clientstate-hmac-key'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-sharepoint-certificate-pfx'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-sharepoint-certificate-pfx'
        identity: workloadIdentity.id
      }
      {
        name: 'microsoft365-sharepoint-certificate-password'
        keyVaultUrl: '${keyVaultBaseUrl}/microsoft365-sharepoint-certificate-password'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-vision-ocr-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-vision-ocr-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-openai-embedding-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-openai-embedding-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-openai-planning-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-openai-planning-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'azure-search-api-key'
        keyVaultUrl: '${keyVaultBaseUrl}/azure-search-api-key'
        identity: workloadIdentity.id
      }
      {
        name: 'member-pii-email-lookup-hmac-key'
        keyVaultUrl: '${keyVaultBaseUrl}/member-pii-email-lookup-hmac-key'
        identity: workloadIdentity.id
      }
    ]

var commonWorkerEnvironmentVariables = [
  {
    name: 'DOTNET_ENVIRONMENT'
    value: 'Certif'
  }
  {
    name: 'AZURE_CLIENT_ID'
    value: workloadIdentity.properties.clientId
  }
  {
    name: 'ConnectionStrings__AssistantCoreDatabase'
    secretRef: 'database-connection'
  }
  {
    name: 'Microsoft365__ConsentCallbackUrl'
    value: '${apiBaseUrl}/api/microsoft365/consent/callback'
  }
  {
    name: 'Microsoft365__ConsentSuccessRedirectUrl'
    value: '${spaBaseUrl}/microsoft365/consent/success'
  }
  {
    name: 'Microsoft365__ConsentErrorRedirectUrl'
    value: '${spaBaseUrl}/microsoft365/consent/error'
  }
  {
    name: 'Microsoft365__WebhookBaseUrl'
    value: apiBaseUrl
  }
  {
    name: 'DataProtectionKeyStorage__BlobStorageUri'
    value: '${dataProtectionKeysStorage.properties.primaryEndpoints.blob}${dataProtectionKeysContainerName}/keys.xml'
  }
  {
    name: 'DataProtectionKeyStorage__KeyVaultKeyUri'
    value: dataProtectionKey.properties.keyUriWithVersion
  }
]

var certifWorkerEnvironmentVariables = [
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
    value: 'https://vision-assistant.cognitiveservices.azure.com/'
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
]

resource worker 'Microsoft.App/containerApps@2024-03-01' = {
  name: workerAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: managedIdentities
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
          env: concat(commonWorkerEnvironmentVariables, certifWorkerEnvironmentVariables)
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
        }
      ]
      scale: {
        // CERTIF continuously reconciles Microsoft 365 content and permissions.
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [acrPullRole, keyVaultSecretsUser, dataProtectionKeysBlobContributor, dataProtectionKeyCryptoUser]
}

resource spa 'Microsoft.App/containerApps@2024-03-01' = {
  name: spaAppName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
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
        customDomains: [
          {
            name: certifSpaCustomDomain
            certificateId: certifSpaCertificateId
            bindingType: 'SniEnabled'
          }
        ]
      }
      registries: registryConfiguration
    }
    template: {
      containers: [
        {
          name: 'spa'
          image: '${acrLoginServer}/assistant-spa:${spaImageTag}'
          env: [
            {
              name: 'SPA_API_BASE_URL'
              value: apiBaseUrl
            }
            {
              name: 'SPA_AUTHENTICATION_MODE'
              value: 'MicrosoftEntra'
            }
            {
              name: 'SPA_LAUNCH_MODE'
              value: 'Certification'
            }
            {
              name: 'SPA_AUTHENTICATION_URL'
              value: '${apiBaseUrl}/local-auth/token'
            }
            {
              name: 'SPA_ENTRA_CLIENT_ID'
              value: spaEntraClientId
            }
            {
              name: 'SPA_ENTRA_AUTHORITY'
              value: '${environment().authentication.loginEndpoint}${azureAdTenantId}'
            }
            {
              name: 'SPA_ENTRA_SCOPE'
              value: 'api://${azureAdApiClientId}/access_as_user'
            }
          ]
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
  dependsOn: [acrPullRole]
}

resource migrationsJob 'Microsoft.App/jobs@2024-03-01' = {
  name: migrationsJobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: managedIdentities
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
          identity: workloadIdentity.id
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrations'
          image: '${acrLoginServer}/assistant-migrations:${backendImageTag}'
          args: ['migrate']
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
              value: 'true'
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
  dependsOn: [acrPullRole, keyVaultSecretsUser]
}

output apiName string = api.name
output apiUrl string = apiBaseUrl
output spaName string = spa.name
output spaUrl string = spaBaseUrl
output workerName string = worker.name
output migrationsJobName string = migrationsJob.name
output keyVaultName string = keyVault.name
output sqlServerName string = sql.outputs.serverName
output sqlDatabaseName string = sql.outputs.databaseName
