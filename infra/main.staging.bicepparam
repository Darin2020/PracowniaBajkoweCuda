using './main.bicep'

// Staging: scales to zero when idle, free database, kept out of search engines.
// Values that differ per run or are not for the repository come from environment variables set by the pipeline.

param environmentName = 'staging'
param containerImage = readEnvironmentVariable('CONTAINER_IMAGE')
param appVersion = readEnvironmentVariable('APP_VERSION')
param registryName = readEnvironmentVariable('REGISTRY_NAME')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')

param minReplicas = 0
param noIndex = true
param sqlUseFreeOffer = true

param monthlyBudget = 10
param budgetStartDate = '2026-11-01'
