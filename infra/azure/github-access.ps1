<#
.SYNOPSIS
  Lets GitHub Actions deploy the API without a stored password (OpenID Connect federation).

.DESCRIPTION
  Creates an app registration that trusts tokens from this repository's "production" environment only, and
  gives it the least access the deploy workflow needs: build images in the registry and update the API app.
  Prints the GitHub variables to add (Settings > Environments > production). Run after deploy.ps1.
#>
[CmdletBinding()]
param(
    [string] $ResourceGroup = 'rg-shapers-prod',
    [string] $Repository = 'LehakoeThinane/Shapers-Church',
    [string] $AppName = 'shapers-github-deploy'
)

$ErrorActionPreference = 'Stop'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed." }
    return $output
}

$account = Invoke-Az account show -o json | ConvertFrom-Json
$appId = Invoke-Az ad app list --display-name $AppName --query '[0].appId' -o tsv
if (-not $appId) {
    $appId = Invoke-Az ad app create --display-name $AppName --query appId -o tsv
    Invoke-Az ad sp create --id $appId -o none
}
$principalId = Invoke-Az ad sp show --id $appId --query id -o tsv

$subject = "repo:${Repository}:environment:production"
$existing = Invoke-Az ad app federated-credential list --id $appId --query "[?subject=='$subject'].name" -o tsv
if (-not $existing) {
    $credential = @{
        name      = 'github-production'
        issuer    = 'https://token.actions.githubusercontent.com'
        subject   = $subject
        audiences = @('api://AzureADTokenExchange')
    } | ConvertTo-Json
    $file = New-TemporaryFile
    try {
        Set-Content -Path $file -Value $credential -Encoding utf8
        Invoke-Az ad app federated-credential create --id $appId --parameters "@$file" -o none
    }
    finally { Remove-Item $file -Force -ErrorAction SilentlyContinue }
}

$registry = Invoke-Az acr list --resource-group $ResourceGroup --query '[0]' -o json | ConvertFrom-Json
$apiApp = Invoke-Az containerapp list --resource-group $ResourceGroup --query '[0]' -o json | ConvertFrom-Json

# Contributor on these two resources only: run image builds in the registry, update the API's image.
foreach ($scope in $registry.id, $apiApp.id) {
    az role assignment create --assignee-object-id $principalId --assignee-principal-type ServicePrincipal `
        --role Contributor --scope $scope -o none 2>$null
}

Write-Host "`nAdd these as variables of the 'production' environment in GitHub (Settings > Environments):" -ForegroundColor Green
[ordered]@{
    AZURE_CLIENT_ID       = $appId
    AZURE_TENANT_ID       = $account.tenantId
    AZURE_SUBSCRIPTION_ID = $account.id
    AZURE_RESOURCE_GROUP  = $ResourceGroup
    AZURE_REGISTRY        = $registry.name
    AZURE_API_APP         = $apiApp.name
    API_URL               = 'https://api.shaperschurch.com'
} | Format-Table -HideTableHeaders | Out-String | Write-Host
Write-Host 'And these secrets (Static Web Apps > Manage deployment token):'
Write-Host '  AZURE_STATIC_WEB_APPS_API_TOKEN    from swa-shapers-prod-web'
Write-Host '  AZURE_STATIC_WEB_APPS_ADMIN_TOKEN  from swa-shapers-prod-admin'
