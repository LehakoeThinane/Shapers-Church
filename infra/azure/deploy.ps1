<#
.SYNOPSIS
  Creates or updates the Shapers Church platform in Azure (see docs/deployment.md and ADR 0015).

.DESCRIPTION
  Safe to run again: keys and passwords are generated on the first run, kept in Key Vault, and reused after
  that (changing them would sign everyone out and break stored codes). Nothing secret is printed except the
  first administrator's password, once, when it is created.

  1. Creates the resource group (South Africa North).
  2. Deploys everything except the API, so the API's identity can be given access to Key Vault first.
  3. Builds the API image inside Azure (no Docker needed on this computer).
  4. Deploys the API and prints the addresses and the DNS records to add in konsoleH.

.EXAMPLE
  ./infra/azure/deploy.ps1 -AdminEmail pastor@shaperschurch.com -AlertEmail team@example.org

.EXAMPLE
  ./infra/azure/deploy.ps1 -AdminEmail pastor@shaperschurch.com -AlertEmail team@example.org -EnableAi -AiMonthlyBudgetZar 300
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AdminEmail,
    # Alerts (API failures, readiness, restarts, database load, cost budget) are emailed here.
    [Parameter(Mandatory)] [string] $AlertEmail,
    # Monthly cost budget in the subscription's billing currency; 0 leaves it out.
    [int] $MonthlyBudget = 2000,
    # How often the "background work needs attention" alert checks, in minutes (5, 10, 15, 30 or 60).
    [ValidateSet(5, 10, 15, 30, 60)] [int] $BackgroundAlertMinutes = 15,
    [string] $ResourceGroup = 'rg-shapers-prod',
    [string] $Location = 'southafricanorth',
    [string] $YouTubeApiKey = '',
    [string] $SiteRebuildToken = '',
    [switch] $UseCustomEmailDomain,
    # YouTube captions as sermon transcripts: the church's Google OAuth client (docs/deployment.md).
    [string] $YouTubeOAuthClientId = '',
    [string] $YouTubeOAuthClientSecret = '',
    [string] $YouTubeOAuthRedirectUri = '',
    # AI help for staff: Azure OpenAI and Speech, paused once the month's estimated spend reaches the budget (ADR 0017).
    [switch] $EnableAi,
    [int] $AiMonthlyBudgetZar = 300
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 stops on any warning az writes (e.g. 'a new Bicep release is available'); show errors only.
$env:AZURE_CORE_ONLY_SHOW_ERRORS = 'true'
$root = Resolve-Path (Join-Path $PSScriptRoot '../..')
$template = Join-Path $PSScriptRoot 'main.bicep'
$vaultName = 'kv-shapers-prod'

function Invoke-Az {
    $output = & az @args
    if ($LASTEXITCODE -ne 0) { throw "az $($args -join ' ') failed." }
    return $output
}

# Works in Windows PowerShell 5.1 and PowerShell 7.
function Get-RandomBytes([int] $count) {
    $buffer = New-Object byte[] $count
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return , $buffer
}

function New-Secret([int] $bytes) {
    return [Convert]::ToBase64String((Get-RandomBytes $bytes))
}

# Letters and digits only, so the password is safe inside a connection string. 56 characters, so the
# small modulo bias over a byte is negligible for a 32-character password.
function New-Password([int] $length = 32) {
    $chars = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    return -join ((Get-RandomBytes $length) | ForEach-Object { $chars[$_ % $chars.Length] })
}

function Use-Existing($value, [scriptblock] $create) {
    if ($value) { return $value }
    return & $create
}

# A missing secret is expected on the first run. Windows PowerShell 5.1 stops on a native command's error output
# under 'Stop', even when it's discarded, so these lookups run under 'Continue' and check the exit code instead.
function Get-ExistingSecret([string] $name) {
    $ErrorActionPreference = 'Continue'
    $value = az keyvault secret show --vault-name $vaultName --name $name --query value -o tsv 2>$null
    if ($LASTEXITCODE -eq 0 -and $value) { return $value }
    return $null
}

# ---------- Who and where ----------

$account = Invoke-Az account show -o json | ConvertFrom-Json
Write-Host "Subscription: $($account.name) ($($account.id))"
Write-Host "Signed in as: $($account.user.name)"
if ((Read-Host 'Deploy the Shapers Church platform to this subscription? (yes/no)') -ne 'yes') { return }

$deployer = Invoke-Az ad signed-in-user show --query id -o tsv
Invoke-Az group create --name $ResourceGroup --location $Location --tags app=shapers environment=production -o none

# ---------- Keys and passwords: reuse when they exist ----------

$vaultExists = (& { $ErrorActionPreference = 'Continue'; az keyvault show --name $vaultName --query name -o tsv 2>$null }) -eq $vaultName
$existing = @{}
foreach ($name in 'postgres-admin-password', 'auth-jwt-signing-key', 'auth-otp-hash-key', 'security-hash-key', 'bootstrap-admin-password') {
    $existing[$name] = if ($vaultExists) { Get-ExistingSecret $name } else { $null }
}

$newAdminPassword = -not $existing['bootstrap-admin-password']
$secrets = @{
    postgresAdminPassword  = Use-Existing $existing['postgres-admin-password'] { New-Password 32 }
    jwtSigningKey          = Use-Existing $existing['auth-jwt-signing-key'] { New-Secret 64 }
    otpHashKey             = Use-Existing $existing['auth-otp-hash-key'] { New-Secret 32 }
    securityHashKey        = Use-Existing $existing['security-hash-key'] { New-Secret 32 }
    bootstrapAdminPassword = Use-Existing $existing['bootstrap-admin-password'] { New-Password 20 }
}

# The cost budget keeps the month it was created in, so running this again doesn't move its start date.
$budgetId = "/subscriptions/$($account.id)/resourceGroups/$ResourceGroup/providers/Microsoft.Consumption/budgets/budget-shapers-prod"
# Not finding it is expected on the first run; Windows PowerShell 5.1 would otherwise stop on az's error output.
$budgetStart = & { $ErrorActionPreference = 'Continue'; az resource show --ids $budgetId --api-version 2023-11-01 --query properties.timePeriod.startDate -o tsv 2>$null }
$budgetStartMonth = if ($LASTEXITCODE -eq 0 -and $budgetStart) { $budgetStart.Substring(0, 7) } else { (Get-Date).ToUniversalTime().ToString('yyyy-MM') }

function Deploy([bool] $withApi, [string] $image) {
    # Secure values go through a temporary parameters file, never the command line.
    $parameters = @{
        '$schema'      = 'https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#'
        contentVersion = '1.0.0.0'
        parameters     = @{
            deployApi              = @{ value = $withApi }
            apiImage               = @{ value = $image }
            deployerObjectId       = @{ value = $deployer }
            adminEmail             = @{ value = $AdminEmail }
            useCustomEmailDomain   = @{ value = [bool] $UseCustomEmailDomain }
            postgresAdminPassword  = @{ value = $secrets.postgresAdminPassword }
            jwtSigningKey          = @{ value = $secrets.jwtSigningKey }
            otpHashKey             = @{ value = $secrets.otpHashKey }
            securityHashKey        = @{ value = $secrets.securityHashKey }
            bootstrapAdminPassword = @{ value = $secrets.bootstrapAdminPassword }
            youTubeApiKey          = @{ value = $YouTubeApiKey }
            siteRebuildToken       = @{ value = $SiteRebuildToken }
            youTubeOAuthClientId     = @{ value = $YouTubeOAuthClientId }
            youTubeOAuthClientSecret = @{ value = $YouTubeOAuthClientSecret }
            youTubeOAuthRedirectUri  = @{ value = $YouTubeOAuthRedirectUri }
            enableAi               = @{ value = [bool] $EnableAi }
            aiMonthlyBudgetZar     = @{ value = $AiMonthlyBudgetZar }
            alertEmail             = @{ value = $AlertEmail }
            monthlyBudget          = @{ value = $MonthlyBudget }
            backgroundAlertMinutes = @{ value = $BackgroundAlertMinutes }
            budgetStartMonth       = @{ value = $budgetStartMonth }
        }
    }
    $file = New-TemporaryFile
    try {
        $parameters | ConvertTo-Json -Depth 5 | Set-Content -Path $file -Encoding utf8
        $result = Invoke-Az deployment group create --resource-group $ResourceGroup --template-file $template `
            --parameters "@$file" --name "shapers-$(Get-Date -Format yyyyMMddHHmmss)" --query properties.outputs -o json
        return $result | ConvertFrom-Json
    }
    finally {
        Remove-Item $file -Force -ErrorAction SilentlyContinue
    }
}

# ---------- 1. Everything except the API ----------

Write-Host "`n[1/3] Creating the database, storage, Key Vault, email, monitoring and static sites (about 10-15 minutes the first time)..."
$outputs = Deploy $false ''

# ---------- 2. Build the API image inside Azure ----------

$tag = Use-Existing (git -C $root rev-parse --short HEAD 2>$null) { Get-Date -Format yyyyMMddHHmm }
$image = "$($outputs.registryLoginServer.value)/shapers-api:$tag"
Write-Host "`n[2/3] Building the API image $image in Azure..."
Invoke-Az acr build --registry $outputs.registryName.value --image "shapers-api:$tag" (Join-Path $root 'backend') -o none

# ---------- 3. The API ----------

Write-Host "`n[3/3] Starting the API..."
$outputs = Deploy $true $image

Write-Host "`nDone." -ForegroundColor Green
Write-Host "API (temporary address):        https://$($outputs.apiDefaultHost.value)/health/ready"
Write-Host "Website (temporary address):    https://$($outputs.websiteDefaultHost.value)"
Write-Host "Admin portal (temporary):       https://$($outputs.adminPortalDefaultHost.value)"
Write-Host "Email is sent from:             $($outputs.emailFrom.value)"
if ($newAdminPassword) {
    Write-Host "`nFirst administrator: $AdminEmail" -ForegroundColor Yellow
    Write-Host "Password (shown once, also in Key Vault as bootstrap-admin-password): $($secrets.bootstrapAdminPassword)" -ForegroundColor Yellow
    Write-Host 'Sign in, set up the authenticator app, then change the password.' -ForegroundColor Yellow
}
Write-Host "`nNext: add the DNS records in konsoleH and connect the custom domains (docs/deployment.md, steps 4-6)."
Write-Host 'Records to verify shaperschurch.com for email:'
$outputs.emailDomainVerificationRecords.value | ConvertTo-Json -Depth 5
