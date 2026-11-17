// Resources shared by every environment (resource group: rg-bajkowecuda-shared).
// - One container registry: the image is built once and the same digest is promoted from staging to production.
// - One Container Apps environment that hosts the staging and the production app (docs/DECISIONS.md, 2026-11-17):
//   the subscription allows a single environment per region.

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

// Container console and system logs of the shared environment. Application telemetry (requests, traces,
// structured logs) goes to the Application Insights of each environment instead, so it stays separated.
resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-bajkowecuda-shared'
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
    // A small daily cap keeps a logging mistake from turning into a bill.
    workspaceCapping: {
      dailyQuotaGb: 1
    }
  }
}

// Consumption only: the environment itself costs nothing, each app is billed for what it uses.
resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: 'cae-bajkowecuda-shared'
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: workspace.properties.customerId
        sharedKey: workspace.listKeys().primarySharedKey
      }
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output containerAppsEnvironmentName string = environment.name
