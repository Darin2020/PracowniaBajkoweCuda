// Key Vault in RBAC mode. The application identity can read secrets and wrap/unwrap with the Data Protection key;
// nobody gets access through access policies.

@minLength(3)
@maxLength(24)
param name string
param location string
param tags object

@description('Principal id of the application managed identity.')
param appPrincipalId string

@description('Purge protection cannot be switched off once it is on, so it is enabled for production only.')
param enablePurgeProtection bool

// Built-in role: Key Vault Secrets User
var secretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'
// Built-in role: Key Vault Crypto Service Encryption User (get, wrap and unwrap keys)
var cryptoServiceEncryptionUserRoleId = 'e147488a-f6f5-4113-8e2d-b22465e65bf6'

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: enablePurgeProtection ? 90 : 7
    // The API accepts only `true` here; leaving the property out keeps purge protection off.
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
  }
}

resource secretsAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, appPrincipalId, secretsUserRoleId)
  scope: vault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', secretsUserRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Wraps the ASP.NET Core Data Protection key ring (session cookies, antiforgery tokens, email links), which is
// stored in Blob Storage. The private key never leaves Key Vault.
resource dataProtectionKey 'Microsoft.KeyVault/vaults/keys@2023-07-01' = {
  parent: vault
  name: 'dataprotection'
  properties: {
    kty: 'RSA'
    keySize: 2048
    keyOps: [
      'wrapKey'
      'unwrapKey'
    ]
  }
}

resource dataProtectionKeyAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataProtectionKey.id, appPrincipalId, cryptoServiceEncryptionUserRoleId)
  scope: dataProtectionKey
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cryptoServiceEncryptionUserRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output name string = vault.name
@description('Versionless URI of the Data Protection key: new key ring entries are wrapped with its latest version.')
output dataProtectionKeyUri string = dataProtectionKey.properties.keyUri
output uri string = vault.properties.vaultUri
