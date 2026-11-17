// User-assigned managed identity of the application. Every Azure service is reached with it:
// no passwords, keys or connection-string secrets (docs/PLAN.md §8.5).

param environmentName string
param location string
param tags object

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-bajkowecuda-${environmentName}'
  location: location
  tags: tags
}

output id string = identity.id
output name string = identity.name
output principalId string = identity.properties.principalId
output clientId string = identity.properties.clientId
