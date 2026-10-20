<#
.SYNOPSIS
  Lets GitHub Actions deploy the API without a stored password (OpenID Connect federation).

.DESCRIPTION
  Creates a user-assigned managed identity that trusts tokens from this repository's "production" environment
  only, and gives it the least access the deploy workflow needs: push images to the registry and update the API
  app. A managed identity needs only Azure access (no directory rights), so it also works for someone invited
  into the church's directory as a guest. Prints the GitHub variables to set (Settings > Environments >
  production). Run after deploy.ps1.

.EXAMPLE
  ./infra/azure/github-access.ps1 -Name shaperschurch
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-shapers-prod',
    # The same name as given to deploy.ps1.
    [string] $Name = 'shapers',
    [string] $Repository = 'LehakoeThinane/Shapers-Church',
    # GitHub now puts IDs in the token's subject (owner@id/repo@id); both forms are trusted.
    [string] $RepositoryWithIds = 'LehakoeThinane@186418078/Shapers-Church@1393760602'
)

$ErrorActionPreference = 'Stop'
$env:AZURE_CORE_ONLY_SHOW_ERRORS = 'true'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed." }
    return $output
}

$account = Invoke-Az account show -o json | ConvertFrom-Json
$identityName = "id-$Name-prod-github"
$identity = Invoke-Az identity create --resource-group $ResourceGroup --name $identityName -o json | ConvertFrom-Json

$credentials = @{
    'github-production'     = "repo:${Repository}:environment:production"
    'github-production-ids' = "repo:${RepositoryWithIds}:environment:production"
}
$existing = Invoke-Az identity federated-credential list --identity-name $identityName --resource-group $ResourceGroup --query '[].name' -o tsv
foreach ($credential in $credentials.GetEnumerator()) {
    if ($existing -notcontains $credential.Key) {
        Invoke-Az identity federated-credential create --identity-name $identityName --resource-group $ResourceGroup `
            --name $credential.Key --issuer 'https://token.actions.githubusercontent.com' --subject $credential.Value `
            --audiences 'api://AzureADTokenExchange' -o none
    }
}

$registry = Invoke-Az acr show --name "cr$($Name)prod" -o json | ConvertFrom-Json
$apiApp = Invoke-Az containerapp show --resource-group $ResourceGroup --name "ca-$Name-prod-api" -o json | ConvertFrom-Json

# Contributor on these two resources only: push images to the registry, update the API's image.
# 'Continue', so Windows PowerShell 5.1 doesn't stop on az's output when the assignment already exists.
foreach ($scope in $registry.id, $apiApp.id) {
    & {
        $ErrorActionPreference = 'Continue'
        az role assignment create --assignee-object-id $identity.principalId --assignee-principal-type ServicePrincipal `
            --role Contributor --scope $scope -o none 2>$null
    }
}

Write-Host "`nSet these as variables of the 'production' environment in GitHub (Settings > Environments):" -ForegroundColor Green
[ordered]@{
    AZURE_CLIENT_ID       = $identity.clientId
    AZURE_TENANT_ID       = $account.tenantId
    AZURE_SUBSCRIPTION_ID = $account.id
    AZURE_RESOURCE_GROUP  = $ResourceGroup
    AZURE_REGISTRY        = $registry.name
    AZURE_API_APP         = $apiApp.name
    API_URL               = 'https://api.shaperschurch.com'
} | Format-Table -HideTableHeaders | Out-String | Write-Host
Write-Host 'And these secrets (Static Web Apps > Manage deployment token):'
Write-Host "  AZURE_STATIC_WEB_APPS_API_TOKEN    from swa-$Name-prod-web"
Write-Host "  AZURE_STATIC_WEB_APPS_ADMIN_TOKEN  from swa-$Name-prod-admin"
