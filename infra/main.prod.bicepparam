using './main.bicep'

// Production: one replica always running (the chat hub and the email worker need it), indexed by search engines.
// Values that differ per run or are not for the repository come from environment variables set by the pipeline.

param environmentName = 'prod'
param containerImage = readEnvironmentVariable('CONTAINER_IMAGE')
param appVersion = readEnvironmentVariable('APP_VERSION')
param registryName = readEnvironmentVariable('REGISTRY_NAME')
param sqlAdminObjectId = readEnvironmentVariable('SQL_ADMIN_OBJECT_ID')
// The pipeline migrates with the new image while the app still runs the current one (two-pass deployment).
param migrationImage = readEnvironmentVariable('MIGRATION_IMAGE', readEnvironmentVariable('CONTAINER_IMAGE'))
param migrationVersion = readEnvironmentVariable('MIGRATION_VERSION', readEnvironmentVariable('APP_VERSION'))

param minReplicas = 1
param noIndex = false

// Owner's decision (2026-11-16): start on the free serverless offer. Switch to `false` for the Basic (DTU) tier
// when the database must be always on; see docs/RUNBOOK.md before doing so.
param sqlUseFreeOffer = true

param monthlyBudget = 20
param budgetStartDate = '2026-11-01'
