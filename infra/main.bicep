// One environment of Pracownia Bajkowe Cuda (resource group: rg-bajkowecuda-staging or rg-bajkowecuda-prod).
// See docs/PLAN.md §10. The deployment is idempotent: the pipeline runs it on every release.

targetScope = 'resourceGroup'

@description('Environment name. It is part of every resource name.')
@allowed(['staging', 'prod'])
param environmentName string

@description('Azure region. Defaults to the region of the resource group.')
param location string = resourceGroup().location

@description('Full image reference to run, pinned by digest: <registry>/<repository>@sha256:...')
param containerImage string

@description('Version shown by /api/version and in the logs: the git commit the image was built from.')
param appVersion string

@description('Name of the shared container registry (output of shared.bicep).')
param registryName string

@description('Resource group of the shared container registry.')
param registryResourceGroup string = 'rg-bajkowecuda-shared'

@description('Object id of the Entra user or group that administers the SQL server.')
param sqlAdminObjectId string

@description('Type of the SQL administrator principal.')
@allowed(['User', 'Group', 'Application'])
param sqlAdminPrincipalType string = 'User'

@description('true: free serverless database (auto-pauses when the monthly free limit is used up). false: Basic (DTU) tier.')
param sqlUseFreeOffer bool = true

@description('Minimum number of replicas. 0 lets the app scale to zero when idle.')
@minValue(0)
@maxValue(1)
param minReplicas int

@description('true keeps the environment out of search engines (X-Robots-Tag and robots.txt).')
param noIndex bool

@description('Public origin of the site. Empty: the default address of the Container App is used.')
param publicBaseUrl string = ''

@description('Protects data against accidental loss: Key Vault purge protection and blob soft delete.')
param enableDataProtection bool = environmentName == 'prod'

@description('Monthly budget of the resource group, in the billing currency.')
@minValue(1)
param monthlyBudget int

@description('First day of the month the budget starts in (yyyy-MM-01). A budget start date cannot be moved later on.')
param budgetStartDate string

var tags = {
  project: 'bajkowecuda'
  environment: environmentName
  managedBy: 'bicep'
}

// Storage account, Key Vault and SQL server names are global: a short stable suffix makes them unique.
var suffix = take(uniqueString(resourceGroup().id), 6)

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

module identity 'modules/identity.bicep' = {
  name: 'identity'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
  }
}

// The registry lives in the shared resource group, so the role assignment is deployed there.
module registryAccess 'modules/registry-pull.bicep' = {
  name: 'registry-pull-${environmentName}'
  scope: resourceGroup(registryResourceGroup)
  params: {
    registryName: registryName
    principalId: identity.outputs.principalId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    name: 'stbajkowe${take(environmentName, 4)}${suffix}'
    location: location
    tags: tags
    appPrincipalId: identity.outputs.principalId
    enableSoftDelete: enableDataProtection
  }
}

module keyVault 'modules/key-vault.bicep' = {
  name: 'key-vault'
  params: {
    name: 'kv-bajkowe-${take(environmentName, 4)}-${suffix}'
    location: location
    tags: tags
    appPrincipalId: identity.outputs.principalId
    enablePurgeProtection: enableDataProtection
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    serverName: 'sql-bajkowecuda-${environmentName}-${suffix}'
    location: location
    tags: tags
    adminObjectId: sqlAdminObjectId
    adminPrincipalType: sqlAdminPrincipalType
    useFreeOffer: sqlUseFreeOffer
  }
}

module communication 'modules/communication.bicep' = {
  name: 'communication'
  params: {
    environmentName: environmentName
    suffix: suffix
    tags: tags
  }
}

module app 'modules/container-app.bicep' = {
  name: 'container-app'
  params: {
    environmentName: environmentName
    location: location
    tags: tags
    containerImage: containerImage
    appVersion: appVersion
    registryLoginServer: registryAccess.outputs.registryLoginServer
    identityId: identity.outputs.id
    identityClientId: identity.outputs.clientId
    logAnalyticsWorkspaceName: monitoring.outputs.workspaceName
    applicationInsightsConnectionString: monitoring.outputs.applicationInsightsConnectionString
    minReplicas: minReplicas
    noIndex: noIndex
    publicBaseUrl: publicBaseUrl
    sqlServerFqdn: sql.outputs.serverFqdn
    sqlDatabaseName: sql.outputs.databaseName
    blobServiceUri: storage.outputs.blobServiceUri
    keyVaultUri: keyVault.outputs.uri
    emailEndpoint: communication.outputs.endpoint
    emailSenderAddress: communication.outputs.senderAddress
  }
}

module budget 'modules/budget.bicep' = {
  name: 'budget'
  params: {
    environmentName: environmentName
    amount: monthlyBudget
    startDate: budgetStartDate
  }
}

@description('Public URL of the application. The pipeline runs the smoke test against it.')
output appUrl string = app.outputs.url

output appName string = app.outputs.name
output identityName string = identity.outputs.name
output sqlServerFqdn string = sql.outputs.serverFqdn
output keyVaultName string = keyVault.outputs.name
