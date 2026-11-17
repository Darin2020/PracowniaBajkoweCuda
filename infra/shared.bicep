// Resources shared by every environment (resource group: rg-bajkowecuda-shared).
// One container registry: the image is built once and the same digest is promoted from staging to production.

targetScope = 'resourceGroup'

@description('Azure region. Defaults to the region of the resource group.')
param location string = resourceGroup().location

var tags = {
  project: 'bajkowecuda'
  environment: 'shared'
  managedBy: 'bicep'
}

// Registry names are global and alphanumeric only, so a short stable suffix makes the name unique.
var registryName = 'crbajkowecuda${take(uniqueString(resourceGroup().id), 6)}'

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: registryName
  location: location
  tags: tags
  sku: {
    name: 'Basic'
  }
  properties: {
    // No admin user: access is by Entra identities and role assignments only.
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
