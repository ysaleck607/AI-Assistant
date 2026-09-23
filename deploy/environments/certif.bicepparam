using '../infra/main.bicep'

param location = 'canadacentral'
param environmentName = 'certif'
param azureSearchIndexName = 'microsoft-content-certif-v2'
param nameSuffix = 'replace'
param backendImageTag = 'sha-replace'
param spaImageTag = 'sha-replace'
