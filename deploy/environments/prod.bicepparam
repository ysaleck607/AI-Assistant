using '../infra/main.bicep'

param location = 'canadacentral'
param environmentName = 'prod'
param nameSuffix = 'onp01'
param backendImageTag = 'sha-replace'
param spaImageTag = 'sha-replace'
param bffEntraClientId = '59c7f6f5-b5a3-43da-a06d-e868c32ac25b'
