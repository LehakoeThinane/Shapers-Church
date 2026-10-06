<#
.SYNOPSIS
  Deploys the temporary demo: API + admin portal on one small Azure VM, database on Supabase.

.DESCRIPTION
  Safe to run again (it updates the existing VM). Everything lives in its own resource group, so the whole
  demo is removed with one command. Keys are generated on the first run and kept in /opt/shapers/.env on the
  VM; the first administrator's password is printed once. See docs/demo.md. Not the production setup
  (that's infra/azure).

.EXAMPLE
  ./infra/demo/deploy-demo.ps1 -AdminEmail you@example.org -WebsiteOrigin https://<site>.azurestaticapps.net
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $AdminEmail,
    [Parameter(Mandatory)] [string] $WebsiteOrigin,
    # Files holding the secrets, kept out of the command line and shell history.
    [string] $DatabaseConnectionFile = (Join-Path $env:LOCALAPPDATA 'shapers-demo/database-connection.txt'),
    [string] $EmailConnectionFile = (Join-Path $env:LOCALAPPDATA 'shapers-demo/email-connection.txt'),
    [string] $EmailFromFile = (Join-Path $env:LOCALAPPDATA 'shapers-demo/email-from.txt'),
    [string] $ResourceGroup = 'rg-shapers-demo',
    # Next to the Supabase database (Ireland); small sizes are restricted for this subscription in some regions.
    [string] $Location = 'ukwest',
    [string] $Size = 'Standard_B2ats_v2',
    [string] $VmName = 'vm-shapers-demo',
    [string] $DnsLabel = 'shapers-demo',
    [string] $SshKey = (Join-Path $env:USERPROFILE '.ssh/shapers_demo')
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$work = Join-Path ([IO.Path]::GetTempPath()) 'shapers-demo-deploy'
New-Item -ItemType Directory -Force $work | Out-Null
$user = 'shapers'

# Runs a native command and fails on its exit code. Progress messages on stderr (docker, az, scp) aren't errors.
function Invoke-Native {
    $ErrorActionPreference = 'Continue'
    & $args[0] $args[1..($args.Length - 1)]
    if ($LASTEXITCODE -ne 0) { throw "$($args -join ' ') failed ($LASTEXITCODE)." }
}

# For commands that are allowed to fail (checking whether something exists yet). Windows PowerShell 5.1 treats
# a native command's error output as fatal under 'Stop', even when it's discarded.
function Invoke-Quiet([scriptblock] $command) {
    $ErrorActionPreference = 'Continue'
    & $command 2>$null
}

$sshOptions = @('-i', $SshKey, '-o', 'StrictHostKeyChecking=accept-new', '-o', 'ConnectTimeout=15', '-o', 'BatchMode=yes')

function Invoke-Vm([string] $command) {
    $ErrorActionPreference = 'Continue'
    $output = ssh @sshOptions "$user@$script:siteHost" $command 2>&1
    if ($LASTEXITCODE -ne 0) { throw "On the VM: $command`n$output" }
    return $output
}

function Send-ToVm([string] $local, [string] $remote) {
    Invoke-Native scp @sshOptions $local "${user}@$($script:siteHost):$remote"
}

function New-Secret([int] $bytes) {
    $buffer = New-Object byte[] $bytes
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return [Convert]::ToBase64String($buffer)
}

function New-Password([int] $length = 20) {
    $chars = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $buffer = New-Object byte[] $length
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    return -join ($buffer | ForEach-Object { $chars[$_ % $chars.Length] })
}

# ---------- 1. The VM ----------

Write-Host "[1/6] Making sure the VM exists ($Size in $Location)..."
Invoke-Native az group create --name $ResourceGroup --location $Location --tags app=shapers environment=demo -o none
$exists = Invoke-Quiet { az vm show --resource-group $ResourceGroup --name $VmName --query name -o tsv }
if (-not $exists) {
    Invoke-Native az vm create --resource-group $ResourceGroup --name $VmName --location $Location `
        --size $Size --image Ubuntu2404 --admin-username $user --ssh-key-values "$SshKey.pub" `
        --os-disk-size-gb 32 --storage-sku Standard_LRS --public-ip-sku Standard `
        --public-ip-address-dns-name $DnsLabel --nsg-rule NONE `
        --custom-data "$PSScriptRoot/vm-startup.sh" --tags app=shapers environment=demo -o none

    # Web traffic from anywhere; SSH only from this computer's current address.
    $myIp = (Invoke-RestMethod 'https://api.ipify.org').Trim()
    $nsg = "${VmName}NSG"
    Invoke-Native az network nsg rule create --resource-group $ResourceGroup --nsg-name $nsg --name web `
        --priority 100 --access Allow --protocol Tcp --direction Inbound --destination-port-ranges 80 443 -o none
    Invoke-Native az network nsg rule create --resource-group $ResourceGroup --nsg-name $nsg --name ssh-admin `
        --priority 110 --access Allow --protocol Tcp --direction Inbound --destination-port-ranges 22 `
        --source-address-prefixes "$myIp/32" -o none
}
$script:siteHost = az network public-ip show --resource-group $ResourceGroup --name "${VmName}PublicIP" --query dnsSettings.fqdn -o tsv
$siteUrl = "https://$script:siteHost"
Write-Host "   Address: $siteUrl"

Write-Host "[2/6] Waiting for Docker on the VM (first boot takes a few minutes)..."
$ready = $false
for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
    Invoke-Quiet { ssh @sshOptions "$user@$script:siteHost" 'docker --version >/dev/null && test -d /opt/shapers' } | Out-Null
    if ($LASTEXITCODE -eq 0) { $ready = $true } else { Start-Sleep 15 }
}
if (-not $ready) { throw 'Docker did not become ready on the VM.' }

# ---------- 2. Settings and keys (reused when they already exist) ----------

Write-Host "[3/6] Preparing settings..."
$current = @{}
$envText = Invoke-Quiet { ssh @sshOptions "$user@$script:siteHost" 'sudo cat /opt/shapers/.env 2>/dev/null || true' }
foreach ($line in $envText) {
    if ($line -match '^([A-Za-z0-9_]+)=(.*)$') { $current[$Matches[1]] = $Matches[2] }
}
function Keep([string] $name, [scriptblock] $create) { if ($current[$name]) { return $current[$name] } return & $create }

$newAdminPassword = -not $current['Auth__BootstrapAdmin__Password']
$adminPassword = Keep 'Auth__BootstrapAdmin__Password' { New-Password 20 }

$settings = [ordered]@{
    SITE_HOST                       = $script:siteHost
    ConnectionStrings__Shapers      = (Get-Content $DatabaseConnectionFile -Raw).Trim()
    Auth__Jwt__SigningKey           = Keep 'Auth__Jwt__SigningKey' { New-Secret 64 }
    Auth__Otp__HashKey              = Keep 'Auth__Otp__HashKey' { New-Secret 32 }
    Security__HashKey               = Keep 'Security__HashKey' { New-Secret 32 }
    Auth__BootstrapAdmin__Email     = $AdminEmail
    Auth__BootstrapAdmin__FirstName = 'Church'
    Auth__BootstrapAdmin__LastName  = 'Administrator'
    Auth__BootstrapAdmin__Password  = $adminPassword
    Email__ConnectionString         = (Get-Content $EmailConnectionFile -Raw).Trim()
    Email__From                     = (Get-Content $EmailFromFile -Raw).Trim()
    Cors__Origins__0                = $WebsiteOrigin
    Communications__PublicApiUrl    = $siteUrl
    Media__Storage__PublicBaseUrl   = $siteUrl
    Events__PublicSiteUrl           = $WebsiteOrigin
    Media__SiteUrl                  = $WebsiteOrigin
    Church__Website                 = $WebsiteOrigin
}
$envFile = Join-Path $work '.env'
($settings.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join "`n" | Set-Content -Path $envFile -NoNewline -Encoding ascii

# ---------- 3. The API image, built here and copied over ----------

Write-Host "[4/6] Building the API image and copying it to the VM..."
Invoke-Native docker build -t shapers-api:demo (Join-Path $root 'backend')
$tar = Join-Path $work 'api.tar'
$gz = "$tar.gz"
Invoke-Native docker save shapers-api:demo -o $tar
$in = [IO.File]::OpenRead($tar); $out = [IO.File]::Create($gz)
$zip = New-Object IO.Compression.GZipStream($out, [IO.Compression.CompressionLevel]::Optimal)
try { $in.CopyTo($zip) } finally { $zip.Dispose(); $out.Dispose(); $in.Dispose() }
Remove-Item $tar
Send-ToVm $gz '~/api.tar.gz'
Remove-Item $gz
Invoke-Vm 'gunzip -c ~/api.tar.gz | sudo docker load && rm ~/api.tar.gz' | Out-Null

# ---------- 4. The admin portal (same address as the API) ----------

Write-Host "[5/6] Building the admin portal..."
Push-Location $root
try {
    $env:VITE_API_URL = ''
    # pnpm.cmd, not pnpm: the PowerShell wrapper (pnpm.ps1) mangles arguments passed as an array.
    Invoke-Native pnpm.cmd --filter @shapers/admin build
}
finally { Remove-Item Env:VITE_API_URL -ErrorAction SilentlyContinue; Pop-Location }
$adminTar = Join-Path $work 'admin.tgz'
Invoke-Native tar -czf $adminTar -C (Join-Path $root 'admin/dist') .
Send-ToVm $adminTar '~/admin.tgz'
Send-ToVm (Join-Path $PSScriptRoot 'docker-compose.yml') '~/docker-compose.yml'
Send-ToVm (Join-Path $PSScriptRoot 'Caddyfile') '~/Caddyfile'
Send-ToVm $envFile '~/shapers.env'
Remove-Item $envFile, $adminTar

# ---------- 5. Start ----------

Write-Host "[6/6] Starting..."
Invoke-Vm 'set -e; cd /opt/shapers; sudo rm -rf admin; sudo mkdir admin; sudo tar -xzf ~/admin.tgz -C admin; rm ~/admin.tgz; sudo mv ~/docker-compose.yml ~/Caddyfile /opt/shapers/; sudo mv ~/shapers.env /opt/shapers/.env; sudo chmod 600 /opt/shapers/.env; sudo docker compose up -d --remove-orphans; sudo docker image prune -f >/dev/null' | Out-Null

$healthy = $false
for ($i = 0; $i -lt 40 -and -not $healthy; $i++) {
    try { $healthy = (Invoke-WebRequest "$siteUrl/health/ready" -UseBasicParsing -TimeoutSec 10).StatusCode -eq 200 }
    catch { Start-Sleep 10 }
}

Write-Host "`nDemo is $(if ($healthy) { 'up' } else { 'deployed, but not answering yet (check: ssh, then sudo docker logs shapers-demo-api-1)' })." -ForegroundColor Green
Write-Host "Admin portal: $siteUrl"
Write-Host "API:          $siteUrl/api"
if ($newAdminPassword) {
    Write-Host "`nFirst administrator: $AdminEmail" -ForegroundColor Yellow
    Write-Host "Password (shown once; also in /opt/shapers/.env on the VM): $adminPassword" -ForegroundColor Yellow
}
