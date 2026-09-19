targetScope = 'resourceGroup'

param location string
param environmentName string
param nameSuffix string
param administratorLogin string
param databaseName string = 'AssistantCoreDb'

@secure()
param administratorPassword string

@allowed([
  'Enabled'
  'Disabled'
])
param publicNetworkAccess string = 'Disabled'
param productionWorkload bool = false

param tags object = {}

var sqlServerName = 'sql-assistant-${environmentName}-${nameSuffix}'

resource sqlServer 'Microsoft.Sql/servers@2023-08-01' = {
  name: sqlServerName
  location: location
  tags: tags
  properties: {
    administratorLogin: administratorLogin
    administratorLoginPassword: administratorPassword
    minimalTlsVersion: '1.2'
    publicNetworkAccess: publicNetworkAccess
    restrictOutboundNetworkAccess: 'Disabled'
    version: '12.0'
  }
}

resource database 'Microsoft.Sql/servers/databases@2023-08-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    autoPauseDelay: productionWorkload ? -1 : 60
    minCapacity: productionWorkload ? json('1') : json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: productionWorkload ? 'Geo' : 'Local'
    useFreeLimit: !productionWorkload
    freeLimitExhaustionBehavior: productionWorkload ? null : 'AutoPause'
  }
}

output serverId string = sqlServer.id
output serverName string = sqlServer.name
output serverFqdn string = sqlServer.properties.fullyQualifiedDomainName
output databaseName string = database.name
