// Blob Storage for chat attachments and order photos, plus the Data Protection key ring. Containers are private and
// files are served only through the API after an authorization check (docs/PLAN.md §8.4).

@minLength(3)
@maxLength(24)
param name string
param location string
param tags object

@description('Principal id of the application managed identity.')
param appPrincipalId string

@description('Keeps deleted blobs and containers for 7 days.')
param enableSoftDelete bool

// Built-in role: Storage Blob Data Contributor
var blobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource account 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: name
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    // Entra identities only: account keys and SAS signed with them are switched off.
    allowSharedKeyAccess: false
    allowBlobPublicAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: account
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: enableSoftDelete
      days: 7
    }
    containerDeleteRetentionPolicy: {
      enabled: enableSoftDelete
      days: 7
    }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for containerName in ['attachments', 'order-photos', 'dataprotection']: {
    parent: blobService
    name: containerName
    properties: {
      publicAccess: 'None'
    }
  }
]

resource blobAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(account.id, appPrincipalId, blobDataContributorRoleId)
  scope: account
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', blobDataContributorRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output blobServiceUri string = account.properties.primaryEndpoints.blob
output dataProtectionBlobUri string = '${account.properties.primaryEndpoints.blob}dataprotection/keys.xml'
