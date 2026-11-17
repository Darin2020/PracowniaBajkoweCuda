// Log Analytics workspace (container logs) + workspace-based Application Insights (traces, metrics, logs).

param environmentName string
param location string
param tags object

resource workspace 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-bajkowecuda-${environmentName}'
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

resource applicationInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-bajkowecuda-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    // Server-side telemetry only; client IP addresses are not stored.
    DisableIpMasking: false
  }
}

output workspaceName string = workspace.name
output applicationInsightsConnectionString string = applicationInsights.properties.ConnectionString
