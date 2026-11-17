<#
.SYNOPSIS
    One-time setup that lets GitHub Actions deploy to Azure without any stored cloud secret (OIDC).

.DESCRIPTION
    Run once by the subscription Owner, after `az login` and `gh auth login`. Safe to run again: every step
    creates what is missing and leaves the rest alone. It creates nothing that costs money.

    What it does:
      1. Registers the resource providers the Bicep templates use.
      2. Creates three resource groups: shared, staging, prod.
      3. Creates two deployment identities (user-assigned managed identities) with a federated credential each,
         so only jobs of this repository that run in the matching GitHub environment can sign in as them:
           - staging identity: deploys the shared registry and staging; reads prod (registry cleanup).
           - production identity: deploys prod. Its GitHub environment requires a reviewer.
      4. Stores the identifiers the workflows need as GitHub secrets (identifiers, not credentials;
         kept as secrets so they are masked in the public logs).

    The application itself is deployed by the CD workflow, not by this script. See docs/RUNBOOK.md.

.PARAMETER Location
    Azure region for the resource groups.

.PARAMETER Repository
    GitHub repository as owner/name.
#>
[CmdletBinding()]
param(
    [string] $Location = 'polandcentral',
    [string] $Repository = 'Darin2020/PracowniaBajkoweCuda'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Project = 'bajkowecuda'
$SharedGroup = "rg-$Project-shared"
$StagingGroup = "rg-$Project-staging"
$ProdGroup = "rg-$Project-prod"
$Providers = @(
    'Microsoft.App', 'Microsoft.ContainerRegistry', 'Microsoft.OperationalInsights', 'Microsoft.Insights',
    'Microsoft.ManagedIdentity', 'Microsoft.Storage', 'Microsoft.KeyVault', 'Microsoft.Sql',
    'Microsoft.Communication', 'Microsoft.Consumption'
)

function Invoke-Native {
    # Runs a command-line tool and fails loudly on a non-zero exit code: a half-finished bootstrap must not
    # look successful. Windows PowerShell turns anything a native tool writes to stderr (even a warning) into
    # an error record, so "Stop" is lifted for the call itself and the exit code decides.
    param([string] $Tool, [string[]] $Arguments, [string] $Label)

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Tool @Arguments
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($exitCode -ne 0) { throw "$Label failed with exit code $exitCode." }
    return $output
}

function Invoke-Az {
    # --only-show-errors: progress notes such as "Registering is still on-going" are not errors.
    Invoke-Native -Tool 'az' -Arguments (@($args) + '--only-show-errors') -Label "az $($args -join ' ')"
}

function Invoke-Gh {
    # The label leaves out the value on purpose: it is the secret being stored.
    Invoke-Native -Tool 'gh' -Arguments @($args) -Label "gh $($args[0]) $($args[1]) $($args[2])" | Out-Null
}

function Invoke-Gh-Output {
    Invoke-Native -Tool 'gh' -Arguments @($args) -Label "gh $($args -join ' ')"
}

function New-DeploymentIdentity {
    param([string] $Name, [string] $GitHubEnvironment)

    Invoke-Az identity create --name $Name --resource-group $SharedGroup --location $Location --tags "project=$Project" --output none
    $identity = Invoke-Az identity show --name $Name --resource-group $SharedGroup --output json | ConvertFrom-Json

    # Only a job of this repository that runs in this GitHub environment can exchange its token for this identity.
    # The subject must match the token exactly. GitHub's subject prefix is either "repo:owner/name" or, for
    # repositories with immutable subjects, "repo:owner@ownerId/name@repoId" (it does not change if the repository
    # is renamed or transferred). The repository reports the prefix it uses; it is read rather than guessed.
    $prefix = Invoke-Gh-Output api "repos/$Repository/actions/oidc/customization/sub" --jq '.sub_claim_prefix // empty'
    if ([string]::IsNullOrWhiteSpace($prefix)) { $prefix = "repo:$Repository" }

    Invoke-Az identity federated-credential create `
        --name "github-$GitHubEnvironment" `
        --identity-name $Name `
        --resource-group $SharedGroup `
        --issuer 'https://token.actions.githubusercontent.com' `
        --subject "${prefix}:environment:$GitHubEnvironment" `
        --audiences 'api://AzureADTokenExchange' `
        --output none

    return $identity
}

function Grant-Role {
    param([string] $PrincipalId, [string] $Role, [string] $ResourceGroup)

    $scope = "/subscriptions/$SubscriptionId/resourceGroups/$ResourceGroup"
    # A new identity can take a moment to appear in the directory, hence the retries.
    foreach ($attempt in 1..6) {
        try {
            Invoke-Az role assignment create --assignee-object-id $PrincipalId --assignee-principal-type ServicePrincipal `
                --role $Role --scope $scope --output none
            return
        }
        catch {
            if ($attempt -eq 6) { throw "Could not assign role '$Role' on ${ResourceGroup}: $_" }
            Start-Sleep -Seconds 10
        }
    }
}

$account = Invoke-Az account show --output json | ConvertFrom-Json
$SubscriptionId = $account.id
Write-Host "Subscription: $($account.name)"
Write-Host "Repository:   $Repository"
Write-Host "Region:       $Location"

Write-Host "`n[1/4] Registering resource providers..."
foreach ($provider in $Providers) {
    Invoke-Az provider register --namespace $provider --output none
}

Write-Host "[2/4] Creating resource groups..."
foreach ($group in @($SharedGroup, $StagingGroup, $ProdGroup)) {
    Invoke-Az group create --name $group --location $Location --tags "project=$Project" --output none
}

Write-Host "[3/4] Creating deployment identities and role assignments..."
$staging = New-DeploymentIdentity -Name "id-$Project-github-staging" -GitHubEnvironment 'staging'
$production = New-DeploymentIdentity -Name "id-$Project-github-prod" -GitHubEnvironment 'production'

# "Role Based Access Control Administrator" is needed because the templates create role assignments
# (the application identity gets AcrPull, blob and Key Vault access).
Grant-Role -PrincipalId $staging.principalId -Role 'Contributor' -ResourceGroup $SharedGroup
Grant-Role -PrincipalId $staging.principalId -Role 'Role Based Access Control Administrator' -ResourceGroup $SharedGroup
Grant-Role -PrincipalId $staging.principalId -Role 'Contributor' -ResourceGroup $StagingGroup
Grant-Role -PrincipalId $staging.principalId -Role 'Role Based Access Control Administrator' -ResourceGroup $StagingGroup
# Read-only on prod: the registry cleanup must know which image production is running.
Grant-Role -PrincipalId $staging.principalId -Role 'Reader' -ResourceGroup $ProdGroup

Grant-Role -PrincipalId $production.principalId -Role 'Contributor' -ResourceGroup $ProdGroup
Grant-Role -PrincipalId $production.principalId -Role 'Role Based Access Control Administrator' -ResourceGroup $ProdGroup
# The prod deployment gives its application identity AcrPull on the shared registry. That is a nested
# deployment in the shared group, which needs write access there as well as the right to assign roles.
Grant-Role -PrincipalId $production.principalId -Role 'Contributor' -ResourceGroup $SharedGroup
Grant-Role -PrincipalId $production.principalId -Role 'Role Based Access Control Administrator' -ResourceGroup $SharedGroup

Write-Host "[4/4] Storing identifiers as GitHub secrets..."
# The signed-in user becomes the Entra administrator of the SQL servers.
$sqlAdminObjectId = Invoke-Az ad signed-in-user show --query id --output tsv

Invoke-Gh secret set AZURE_TENANT_ID --repo $Repository --body $account.tenantId
Invoke-Gh secret set AZURE_SUBSCRIPTION_ID --repo $Repository --body $SubscriptionId
Invoke-Gh secret set SQL_ADMIN_OBJECT_ID --repo $Repository --body $sqlAdminObjectId
Invoke-Gh secret set AZURE_CLIENT_ID --repo $Repository --env staging --body $staging.clientId
Invoke-Gh secret set AZURE_CLIENT_ID --repo $Repository --env production --body $production.clientId

Write-Host "`nDone. Next: merge to main (or run the CD workflow manually) to deploy staging."
