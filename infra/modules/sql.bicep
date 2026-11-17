// Azure SQL logical server with Entra-only authentication (there is no SQL password to leak) and one database.

param serverName string
param location string
param tags object

@description('Object id of the Entra principal that administers the server.')
param adminObjectId string

@allowed(['User', 'Group', 'Application'])
param adminPrincipalType string

@description('true: free serverless offer. false: Basic (DTU) tier.')
param useFreeOffer bool

var databaseName = 'BajkoweCuda'

resource server 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: serverName
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      // The login is only a label; the principal is identified by its object id.
      login: 'bajkowecuda-sql-admin'
      sid: adminObjectId
      principalType: adminPrincipalType
      tenantId: tenant().tenantId
    }
  }
}

// The special 0.0.0.0 rule admits traffic that originates inside Azure (the Container App) and nothing else.
resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: server
  name: 'AllowAllWindowsAzureIps'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

// Free offer: General Purpose serverless with a monthly free allowance. When it is used up the database
// pauses until the next month instead of producing a bill.
resource freeDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (useFreeOffer) {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5_2'
    tier: 'GeneralPurpose'
  }
  properties: {
    useFreeLimit: true
    freeLimitExhaustionBehavior: 'AutoPause'
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 34359738368
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

// Basic (DTU): a small fixed price, always on, no cold start.
resource basicDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = if (!useFreeOffer) {
  parent: server
  name: databaseName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
    tier: 'Basic'
  }
  properties: {
    maxSizeBytes: 2147483648
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = databaseName
