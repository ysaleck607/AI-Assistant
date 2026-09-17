using '../infra/main.bicep'

param location = 'canadacentral'
param environmentName = 'certif'
param azureSearchIndexName = 'microsoft-content-certif'
param nameSuffix = 'replace'
param sharedResourceGroupName = 'rg-assistant-shared'
param backendImageTag = 'sha-replace'
param spaImageTag = 'sha-replace'
