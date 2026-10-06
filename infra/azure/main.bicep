// Shapers Church platform on Azure (ADR 0015). One resource group, South Africa North (Johannesburg).
//
//   API (Container Apps, always on) ── private network ── PostgreSQL Flexible Server (backups, pgvector)
//        │  secrets from Key Vault through a managed identity (no passwords in config)
//        ├─ Blob Storage (sermon audio, PDFs, images)
//        ├─ Communication Services (email)
//        └─ Application Insights (logs, traces, metrics through the platform's OpenTelemetry agent)
//   Static Web Apps: the public website (shaperschurch.com) and the admin portal (admin.shaperschurch.com)
//
// Deployed by infra/azure/deploy.ps1, which generates the keys on first run and reuses them afterwards.
// See docs/deployment.md.

targetScope = 'resourceGroup'

@description('Azure region for everything that holds data.')
param location string = 'southafricanorth'

@description('Region for the Static Web Apps resources (their content is served worldwide; they hold no personal data). West Europe was closed to new customers in October 2026.')
param staticSitesLocation string = 'eastus2'

@description('Short name used in resource names.')
@minLength(3)
@maxLength(12)
param name string = 'shapers'

@description('First deployment creates everything except the API, so the API identity can be given access to Key Vault first.')
param deployApi bool = true

@description('Container image for the API, e.g. crshapersprod.azurecr.io/shapers-api:<git sha>.')
param apiImage string = ''

@description('Object ID of the person or pipeline running the deployment; gets permission to manage Key Vault secrets.')
param deployerObjectId string

@description('Email of the first church administrator (gets the Church administrator role on first start).')
param adminEmail string

@description('Send email from shaperschurch.com (after its DNS records are verified) instead of the Azure-provided address.')
param useCustomEmailDomain bool = false

param postgresAdminLogin string = 'shapersadmin'

@secure()
param postgresAdminPassword string

@secure()
param jwtSigningKey string

@secure()
param otpHashKey string

@secure()
param securityHashKey string

@secure()
param bootstrapAdminPassword string

@description('Optional: YouTube Data API key for importing sermons.')
@secure()
param youTubeApiKey string = ''

@description('Optional: GitHub token allowed to trigger the website rebuild (repository dispatch).')
@secure()
param siteRebuildToken string = ''

@description('AI help for staff (sermon transcripts, drafts). Creates Azure OpenAI and Speech in this region; the API signs in with its managed identity.')
param enableAi bool = false

@description('Estimated monthly AI spend after which AI help pauses until the 1st.')
param aiMonthlyBudgetZar int = 300

var suffix = '${name}-prod'
var compact = '${name}prod'
var tags = { app: 'shapers', environment: 'production' }

// Built-in role IDs.
var roles = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultSecretsOfficer: 'b86a8fe4-44ce-4948-aee5-eccb2c155cd7'
  acrPull: '7f951dda-4ed3-4ff4-a3b3-0b2a7dc2a1c8'
  cognitiveServicesOpenAiUser: '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
  cognitiveServicesUser: 'a97b65f3-24c7-4388-baec-2e87135dc908'
}

// ---------- Monitoring ----------

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
  }
}

// ---------- Network: the database is only reachable from the API ----------

resource vnet 'Microsoft.Network/virtualNetworks@2024-05-01' = {
  name: 'vnet-${suffix}'
  location: location
  tags: tags
  properties: {
    addressSpace: { addressPrefixes: ['10.20.0.0/16'] }
    subnets: [
      {
        name: 'apps'
        properties: {
          addressPrefix: '10.20.0.0/23'
          delegations: [{ name: 'apps', properties: { serviceName: 'Microsoft.App/environments' } }]
        }
      }
      {
        name: 'database'
        properties: {
          addressPrefix: '10.20.2.0/24'
          delegations: [{ name: 'database', properties: { serviceName: 'Microsoft.DBforPostgreSQL/flexibleServers' } }]
        }
      }
    ]
  }
}

resource databaseDns 'Microsoft.Network/privateDnsZones@2024-06-01' = {
  name: '${suffix}.private.postgres.database.azure.com'
  location: 'global'
  tags: tags
}

resource databaseDnsLink 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2024-06-01' = {
  parent: databaseDns
  name: 'vnet'
  location: 'global'
  properties: {
    virtualNetwork: { id: vnet.id }
    registrationEnabled: false
  }
}

// ---------- Database ----------

resource postgres 'Microsoft.DBforPostgreSQL/flexibleServers@2024-08-01' = {
  name: 'psql-${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard_B1ms', tier: 'Burstable' }
  properties: {
    version: '17'
    administratorLogin: postgresAdminLogin
    administratorLoginPassword: postgresAdminPassword
    storage: { storageSizeGB: 32, autoGrow: 'Enabled' }
    backup: { backupRetentionDays: 14, geoRedundantBackup: 'Disabled' }
    highAvailability: { mode: 'Disabled' }
    network: {
      delegatedSubnetResourceId: '${vnet.id}/subnets/database'
      privateDnsZoneArmResourceId: databaseDns.id
      publicNetworkAccess: 'Disabled'
    }
  }
  dependsOn: [databaseDnsLink]
}

// pgvector is allowed now for semantic search later (V3).
resource postgresExtensions 'Microsoft.DBforPostgreSQL/flexibleServers/configurations@2024-08-01' = {
  parent: postgres
  name: 'azure.extensions'
  properties: { value: 'VECTOR', source: 'user-override' }
}

resource database 'Microsoft.DBforPostgreSQL/flexibleServers/databases@2024-08-01' = {
  parent: postgres
  name: 'shapers'
  properties: { charset: 'UTF8', collation: 'en_US.utf8' }
}

// ---------- Files ----------

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'st${compact}'
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    // Published sermon audio, notes and images are public; everything else stays private.
    allowBlobPublicAccess: true
    allowSharedKeyAccess: true
  }
}

resource blobs 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
  properties: {
    deleteRetentionPolicy: { enabled: true, days: 14 }
    // The admin portal uploads straight to storage with a short-lived signed URL.
    cors: {
      corsRules: [
        {
          allowedOrigins: ['https://admin.shaperschurch.com']
          allowedMethods: ['PUT', 'GET', 'HEAD', 'OPTIONS']
          allowedHeaders: ['*']
          exposedHeaders: ['*']
          maxAgeInSeconds: 3600
        }
      ]
    }
  }
}

resource mediaContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: blobs
  name: 'media'
  properties: { publicAccess: 'Blob' }
}

// The keys that protect staff sign-in cookies, kept across restarts and deployments.
resource files 'Microsoft.Storage/storageAccounts/fileServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource keysShare 'Microsoft.Storage/storageAccounts/fileServices/shares@2023-05-01' = {
  parent: files
  name: 'api-keys'
  properties: { shareQuota: 1 }
}

// ---------- Email ----------

resource emailService 'Microsoft.Communication/emailServices@2023-04-01' = {
  name: 'ecs-${suffix}'
  location: 'global'
  tags: tags
  properties: { dataLocation: 'Africa' }
}

// Works immediately; replaced by the church's own domain once its DNS records are verified.
resource azureManagedDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  properties: { domainManagement: 'AzureManaged', userEngagementTracking: 'Disabled' }
}

resource churchDomain 'Microsoft.Communication/emailServices/domains@2023-04-01' = {
  parent: emailService
  name: 'shaperschurch.com'
  location: 'global'
  properties: { domainManagement: 'CustomerManaged', userEngagementTracking: 'Disabled' }
}

resource communication 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: 'acs-${suffix}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Africa'
    linkedDomains: [useCustomEmailDomain ? churchDomain.id : azureManagedDomain.id]
  }
}

var emailFrom = useCustomEmailDomain ? 'DoNotReply@shaperschurch.com' : 'DoNotReply@${azureManagedDomain.properties.mailFromSenderDomain}'

// ---------- Container registry and identity ----------

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'cr${compact}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource apiIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: 'id-${suffix}-api'
  location: location
  tags: tags
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: registry
  name: guid(registry.id, apiIdentity.id, roles.acrPull)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.acrPull)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- AI help (optional) ----------
// Accounts in South Africa North; the writing model is only offered as Global Standard, so prompts may be processed
// outside South Africa. Only public church content is sent (ADR 0017). Keys are disabled: the API uses its identity.

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (enableAi) {
  name: 'oai-${suffix}'
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'oai-${suffix}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource chatModel 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = if (enableAi) {
  parent: openAi
  name: 'gpt-5.4-mini'
  sku: { name: 'GlobalStandard', capacity: 50 }
  properties: {
    model: { format: 'OpenAI', name: 'gpt-5.4-mini', version: '2026-03-17' }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

resource speech 'Microsoft.CognitiveServices/accounts@2024-10-01' = if (enableAi) {
  name: 'spch-${suffix}'
  location: location
  tags: tags
  kind: 'SpeechServices'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: 'spch-${suffix}'
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
  }
}

resource openAiApiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAi) {
  scope: openAi
  name: guid(openAi.id, apiIdentity.id, roles.cognitiveServicesOpenAiUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesOpenAiUser)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource speechApiUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (enableAi) {
  scope: speech
  name: guid(speech.id, apiIdentity.id, roles.cognitiveServicesUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.cognitiveServicesUser)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------- Secrets ----------

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${suffix}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
  }
}

resource vaultApiReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, apiIdentity.id, roles.keyVaultSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
    principalId: apiIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource vaultDeployer 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, deployerObjectId, roles.keyVaultSecretsOfficer)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsOfficer)
    principalId: deployerObjectId
  }
}

var connectionString = 'Host=${postgres.properties.fullyQualifiedDomainName};Database=${database.name};Username=${postgresAdminLogin};Password=${postgresAdminPassword};SSL Mode=VerifyFull'
var storageConnection = 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

// Which secrets exist is known up front (optional ones only when a value was given); their values are filled in
// during the deployment.
var secretNames = concat(
  [
    'postgres-admin-password'
    'connection-string-shapers'
    'auth-jwt-signing-key'
    'auth-otp-hash-key'
    'security-hash-key'
    'bootstrap-admin-password'
    'storage-connection-string'
    'email-connection-string'
  ],
  empty(youTubeApiKey) ? [] : ['youtube-api-key'],
  empty(siteRebuildToken) ? [] : ['site-rebuild-token']
)
var secretValues = {
  'postgres-admin-password': postgresAdminPassword
  'connection-string-shapers': connectionString
  'auth-jwt-signing-key': jwtSigningKey
  'auth-otp-hash-key': otpHashKey
  'security-hash-key': securityHashKey
  'bootstrap-admin-password': bootstrapAdminPassword
  'storage-connection-string': storageConnection
  'email-connection-string': communication.listKeys().primaryConnectionString
  'youtube-api-key': youTubeApiKey
  'site-rebuild-token': siteRebuildToken
}

resource secrets 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = [
  for secretName in secretNames: {
    parent: vault
    name: secretName
    properties: { value: secretValues[secretName] }
  }
]

// ---------- API ----------

resource appsEnvironment 'Microsoft.App/managedEnvironments@2024-10-02-preview' = {
  name: 'cae-${suffix}'
  location: location
  tags: tags
  properties: {
    vnetConfiguration: { infrastructureSubnetId: '${vnet.id}/subnets/apps', internal: false }
    workloadProfiles: [{ name: 'Consumption', workloadProfileType: 'Consumption' }]
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
    // The platform's OpenTelemetry agent forwards the API's traces and logs to Application Insights.
    appInsightsConfiguration: { connectionString: insights.properties.ConnectionString }
    openTelemetryConfiguration: {
      tracesConfiguration: { destinations: ['appInsights'] }
      logsConfiguration: { destinations: ['appInsights'] }
    }
  }
}

resource keysStorage 'Microsoft.App/managedEnvironments/storages@2024-10-02-preview' = {
  parent: appsEnvironment
  name: 'api-keys'
  properties: {
    azureFile: {
      accountName: storage.name
      accountKey: storage.listKeys().keys[0].value
      shareName: keysShare.name
      accessMode: 'ReadWrite'
    }
  }
}

var secretRefs = [for secretName in secretNames: {
  name: secretName
  keyVaultUrl: 'https://${vault.name}${environment().suffixes.keyvaultDns}/secrets/${secretName}'
  identity: apiIdentity.id
}]

var baseEnv = [
  { name: 'ConnectionStrings__Shapers', secretRef: 'connection-string-shapers' }
  { name: 'Auth__Jwt__SigningKey', secretRef: 'auth-jwt-signing-key' }
  { name: 'Auth__Otp__HashKey', secretRef: 'auth-otp-hash-key' }
  { name: 'Security__HashKey', secretRef: 'security-hash-key' }
  { name: 'Auth__BootstrapAdmin__Email', value: adminEmail }
  { name: 'Auth__BootstrapAdmin__FirstName', value: 'Church' }
  { name: 'Auth__BootstrapAdmin__LastName', value: 'Administrator' }
  { name: 'Auth__BootstrapAdmin__Password', secretRef: 'bootstrap-admin-password' }
  { name: 'Media__Storage__ConnectionString', secretRef: 'storage-connection-string' }
  { name: 'Email__ConnectionString', secretRef: 'email-connection-string' }
  { name: 'Email__From', value: emailFrom }
  { name: 'DataProtection__KeysPath', value: '/app/App_Data/keys' }
]
var optionalEnv = concat(
  empty(youTubeApiKey) ? [] : [{ name: 'Media__YouTube__ApiKey', secretRef: 'youtube-api-key' }],
  empty(siteRebuildToken) ? [] : [{ name: 'Content__SiteRebuild__Token', secretRef: 'site-rebuild-token' }],
  enableAi
    ? [
        { name: 'Assist__Provider', value: 'Azure' }
        { name: 'Assist__MonthlyBudgetZar', value: string(aiMonthlyBudgetZar) }
        { name: 'Assist__Azure__OpenAiEndpoint', value: openAi!.properties.endpoint }
        { name: 'Assist__Azure__ChatDeployment', value: chatModel.name }
        { name: 'Assist__Azure__SpeechEndpoint', value: speech!.properties.endpoint }
        // DefaultAzureCredential uses this user-assigned identity.
        { name: 'AZURE_CLIENT_ID', value: apiIdentity.properties.clientId }
      ]
    : []
)

resource api 'Microsoft.App/containerApps@2025-01-01' = if (deployApi) {
  name: 'ca-${suffix}-api'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${apiIdentity.id}': {} }
  }
  properties: {
    environmentId: appsEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        allowInsecure: false
      }
      registries: [{ server: registry.properties.loginServer, identity: apiIdentity.id }]
      secrets: secretRefs
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: { cpu: json('0.5'), memory: '1Gi' }
          env: concat(baseEnv, optionalEnv)
          volumeMounts: [{ volumeName: 'api-keys', mountPath: '/app/App_Data/keys' }]
          probes: [
            // Migrations run on start, so the first start gets a few minutes.
            { type: 'Startup', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 10, failureThreshold: 30 }
            { type: 'Liveness', httpGet: { path: '/health/live', port: 8080 }, periodSeconds: 30 }
            { type: 'Readiness', httpGet: { path: '/health/ready', port: 8080 }, periodSeconds: 15 }
          ]
        }
      ]
      volumes: [{ name: 'api-keys', storageType: 'AzureFile', storageName: keysStorage.name }]
      // One always-on copy: background jobs and live chat must keep running, and live chat has no
      // backplane yet for more than one copy (see docs/deployment.md before raising maxReplicas).
      scale: { minReplicas: 1, maxReplicas: 1 }
    }
  }
  dependsOn: [acrPull, vaultApiReader, secrets]
}

// ---------- Static sites ----------

resource website 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${suffix}-web'
  location: staticSitesLocation
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {}
}

resource adminPortal 'Microsoft.Web/staticSites@2024-04-01' = {
  name: 'swa-${suffix}-admin'
  location: staticSitesLocation
  tags: tags
  sku: { name: 'Free', tier: 'Free' }
  properties: {}
}

// ---------- Outputs for the deploy script and the DNS checklist ----------

output registryLoginServer string = registry.properties.loginServer
output registryName string = registry.name
output keyVaultName string = vault.name
output apiAppName string = deployApi ? api!.name : ''
output apiDefaultHost string = deployApi ? api!.properties.configuration.ingress.fqdn : ''
output websiteName string = website.name
output websiteDefaultHost string = website.properties.defaultHostname
output adminPortalName string = adminPortal.name
output adminPortalDefaultHost string = adminPortal.properties.defaultHostname
output emailFrom string = emailFrom
output emailDomainVerificationRecords object = churchDomain.properties.verificationRecords
