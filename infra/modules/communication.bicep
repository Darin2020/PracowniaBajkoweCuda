// Azure Communication Services Email with an Azure-managed sender domain (works without owning a domain).
// A custom domain with SPF/DKIM/DMARC replaces it once the owner has one (docs/PLAN.md, Phase 8).

param environmentName string
param suffix string
param tags object

@description('Principal id of the application managed identity, which sends the emails.')
param appPrincipalId string

@description('Where the service stores data at rest.')
param dataLocation string = 'Europe'

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'email-bajkowecuda-${environmentName}-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
  }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource communicationService 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-bajkowecuda-${environmentName}-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: dataLocation
    linkedDomains: [
      managedDomain.id
    ]
  }
}

// Built-in role: Communication and Email Service Owner. Azure Communication Services does not support custom roles;
// this is the role Microsoft documents for sending email with a managed identity.
var emailServiceOwnerRoleId = '09976791-48a7-449e-bb21-39d1a415f350'

resource sendAccess 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communicationService.id, appPrincipalId, emailServiceOwnerRoleId)
  scope: communicationService
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', emailServiceOwnerRoleId)
    principalId: appPrincipalId
    principalType: 'ServicePrincipal'
  }
}

output endpoint string = 'https://${communicationService.properties.hostName}'
output senderAddress string = 'DoNotReply@${managedDomain.properties.mailFromSenderDomain}'
