// Container Apps job that applies the EF Core migrations (`dotnet BajkoweCuda.Api.dll migrate`), started by the
// pipeline before a new app revision goes live. It runs the same image as the app, as the migration identity: a
// member of the SQL administrators group (scripts/azure-bootstrap.ps1). The app identity is only used to pull the
// image; the job gives it a database user with read/write access (docs/DECISIONS.md, 2026-11-17).

param environmentName string
param location string
param tags object

@description('Full image reference, pinned by digest.')
param containerImage string
param appVersion string
param registryLoginServer string

param containerAppsEnvironmentName string
param sharedResourceGroup string

@description('Resource id of the application identity (pulls the image; gets a database user).')
param appIdentityId string
param appIdentityName string
param appIdentityClientId string

@description('Name of the migration identity, created by scripts/azure-bootstrap.ps1 in this resource group.')
param migratorIdentityName string

@secure()
param applicationInsightsConnectionString string

param sqlServerFqdn string
param sqlDatabaseName string

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
  scope: resourceGroup(sharedResourceGroup)
}

resource migrator 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' existing = {
  name: migratorIdentityName
}

resource job 'Microsoft.App/jobs@2024-03-01' = {
  // Job names are limited to 32 characters.
  name: 'caj-bajkowecuda-migrate-${environmentName}'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${appIdentityId}': {}
      '${migrator.id}': {}
    }
  }
  properties: {
    environmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Manual'
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      // A failed migration is not retried blindly: the pipeline stops and a person looks at the logs.
      replicaRetryLimit: 0
      // The serverless database may need a minute to resume before the migration starts.
      replicaTimeout: 900
      registries: [
        {
          server: registryLoginServer
          identity: appIdentityId
        }
      ]
      secrets: [
        {
          name: 'applicationinsights-connection-string'
          value: applicationInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'migrate'
          image: containerImage
          args: ['migrate']
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'DOTNET_ENVIRONMENT', value: 'Production' }
            { name: 'APP_VERSION', value: appVersion }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'applicationinsights-connection-string' }
            { name: 'AZURE_CLIENT_ID', value: migrator.properties.clientId }
            {
              name: 'ConnectionStrings__Sql'
              value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${migrator.properties.clientId};Encrypt=True;TrustServerCertificate=False'
            }
            { name: 'Database__AppUserName', value: appIdentityName }
            { name: 'Database__AppUserClientId', value: appIdentityClientId }
          ]
        }
      ]
    }
  }
}

output name string = job.name
