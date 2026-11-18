// The application: API, SignalR hub and the prerendered site in one container (docs/PLAN.md §4).
// It runs in the shared Container Apps environment of shared.bicep.

param environmentName string
param location string
param tags object

@description('Full image reference, pinned by digest.')
param containerImage string
param appVersion string
param registryLoginServer string

@description('Resource id of the application managed identity.')
param identityId string

@description('Client id of the application managed identity (selects it in DefaultAzureCredential).')
param identityClientId string

@description('Name of the shared Container Apps environment.')
param containerAppsEnvironmentName string

@description('Resource group of the shared Container Apps environment.')
param sharedResourceGroup string

@secure()
param applicationInsightsConnectionString string

@minValue(0)
@maxValue(1)
param minReplicas int
param noIndex bool

@description('Public origin of the site. Empty: the default address of the Container App.')
param publicBaseUrl string

param sqlServerFqdn string
param sqlDatabaseName string
param blobServiceUri string
param keyVaultUri string

@description('Blob that holds the Data Protection key ring, and the Key Vault key that wraps it.')
param dataProtectionBlobUri string
param dataProtectionKeyUri string
param emailEndpoint string
param emailSenderAddress string

var appName = 'ca-bajkowecuda-${environmentName}'
var containerPort = 8080

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' existing = {
  name: containerAppsEnvironmentName
  scope: resourceGroup(sharedResourceGroup)
}

// The default address is known from the environment's domain, so the app can be told its own public origin.
var effectivePublicBaseUrl = empty(publicBaseUrl)
  ? 'https://${appName}.${environment.properties.defaultDomain}'
  : publicBaseUrl

resource app 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityId}': {}
    }
  }
  properties: {
    managedEnvironmentId: environment.id
    workloadProfileName: 'Consumption'
    configuration: {
      // One revision serves all traffic. Older revisions are kept inactive for a quick rollback.
      activeRevisionsMode: 'Single'
      maxInactiveRevisions: 5
      ingress: {
        external: true
        targetPort: containerPort
        transport: 'auto'
        // TLS ends at the ingress; plain HTTP is redirected to HTTPS.
        allowInsecure: false
      }
      registries: [
        {
          server: registryLoginServer
          identity: identityId
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
          name: 'app'
          image: containerImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            // Trust X-Forwarded-Proto / X-Forwarded-For from the ingress (scheme for HSTS, client address).
            { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
            { name: 'APP_VERSION', value: appVersion }
            { name: 'PublicBaseUrl', value: effectivePublicBaseUrl }
            { name: 'Seo__NoIndex', value: string(noIndex) }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'applicationinsights-connection-string' }
            // The settings below carry no secrets: every service is reached with the managed identity.
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            {
              name: 'ConnectionStrings__Sql'
              value: 'Server=tcp:${sqlServerFqdn},1433;Database=${sqlDatabaseName};Authentication=Active Directory Managed Identity;User Id=${identityClientId};Encrypt=True;TrustServerCertificate=False'
            }
            { name: 'Storage__BlobServiceUri', value: blobServiceUri }
            { name: 'KeyVault__Uri', value: keyVaultUri }
            { name: 'DataProtection__BlobUri', value: dataProtectionBlobUri }
            { name: 'DataProtection__KeyUri', value: dataProtectionKeyUri }
            { name: 'Email__Provider', value: 'Acs' }
            { name: 'Email__Acs__Endpoint', value: emailEndpoint }
            { name: 'Email__From', value: emailSenderAddress }
          ]
          probes: [
            // /health/live runs no dependency checks, so probes never wake the serverless database.
            {
              type: 'Startup'
              httpGet: {
                path: '/health/live'
                port: containerPort
              }
              periodSeconds: 3
              failureThreshold: 20
            }
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: containerPort
              }
              periodSeconds: 30
              failureThreshold: 3
            }
          ]
        }
      ]
      scale: {
        minReplicas: minReplicas
        // SignalR and the outbox worker run in-process, so there is exactly one replica (docs/PLAN.md §10).
        maxReplicas: 1
      }
    }
  }
}

output name string = app.name
output url string = effectivePublicBaseUrl
output defaultUrl string = 'https://${app.properties.configuration.ingress.fqdn}'
