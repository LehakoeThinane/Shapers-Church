# Deploying to production (Azure)

How the live system is set up, how to deploy it the first time, and how to look after it. The decisions behind it are in [ADR 0015](decisions/0015-production-deployment.md).

```
shaperschurch.com        Azure Static Web Apps (public website, rebuilt hourly and when content is published)
admin.shaperschurch.com  Azure Static Web Apps (admin portal)
api.shaperschurch.com    Azure Container Apps (API, background jobs, live chat), South Africa North
                           ├─ PostgreSQL Flexible Server (private network only, 14-day backups)
                           ├─ Blob Storage (sermon audio, notes, images)
                           ├─ Key Vault (every key and password)
                           ├─ Communication Services (email, Africa data location)
                           └─ Application Insights (logs, traces)
konsoleH (xneelo)        DNS and the church's email only
```

## What you need first
- An **Azure subscription in the church's name**, with billing set up or the [Microsoft nonprofit grant](https://www.microsoft.com/nonprofits). You need **Owner** on it for the first deployment.
- The [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az login`, then `az bicep install`).
- Access to **konsoleH** (Manage DNS) and admin rights on the **GitHub** repository.
- The **email address of the first church administrator**.

## First deployment

1. **Sign in to the church's subscription.**
   ```powershell
   az login
   az account set --subscription "<church subscription name or id>"
   ```
2. **Deploy.** This takes 15–25 minutes the first time. It asks for confirmation before changing anything.
   ```powershell
   ./infra/azure/deploy.ps1 -AdminEmail admin@shaperschurch.com -AlertEmail <team email>
   ```
   `-AlertEmail` is where alerts go (see *Alerts* below). Until the church has a team address, use the address of whoever looks after the platform. The repository is public, so the address is passed here and not stored in the code. `-MonthlyBudget` sets the cost budget (default 2000, in the subscription's billing currency; 0 leaves it out).
   Add `-YouTubeApiKey <key>` to enable the sermon import, and `-SiteRebuildToken <token>` (a GitHub fine-grained token with *Contents: read & write* on this repository) so the website rebuilds the moment something is published.
   For **sermon transcripts from YouTube captions**, add `-YouTubeOAuthClientId`, `-YouTubeOAuthClientSecret` and `-YouTubeOAuthRedirectUri https://api.shaperschurch.com/api/media/youtube/callback` (see *YouTube captions* below).
   Add `-EnableAi` (optionally `-AiMonthlyBudgetZar 300`) for AI help: it creates Azure OpenAI and Speech in South Africa North and lets the API use them with its managed identity, so no keys are involved (ADR 0017). AI pauses for the rest of the month once the estimated spend reaches the budget.
   The script prints the first administrator's password **once**. Sign in, set up the authenticator app, then change the password.
3. **Check it.** Open the API's temporary address the script printed, ending in `/health/ready`. It should answer `Healthy`.
4. **Connect `api.shaperschurch.com`.** Get the verification ID:
   ```powershell
   az containerapp show -g rg-shapers-prod -n ca-shapers-prod-api --query properties.customDomainVerificationId -o tsv
   ```
   Add the DNS records in konsoleH (table below), wait about 10 minutes, then:
   ```powershell
   az containerapp hostname add  -g rg-shapers-prod -n ca-shapers-prod-api --hostname api.shaperschurch.com
   az containerapp hostname bind -g rg-shapers-prod -n ca-shapers-prod-api --hostname api.shaperschurch.com `
     --environment cae-shapers-prod --validation-method CNAME
   ```
   Azure issues and renews the HTTPS certificate for free.
5. **Connect the websites.** Add the CNAME records, then:
   ```powershell
   az staticwebapp hostname set -n swa-shapers-prod-admin --hostname admin.shaperschurch.com
   az staticwebapp hostname set -n swa-shapers-prod-web   --hostname www.shaperschurch.com
   ```
   **The bare domain (`shaperschurch.com`):** Static Web Apps needs an ALIAS or "CNAME flattening" record at the root, which konsoleH may not offer. Either redirect `shaperschurch.com` to `www.shaperschurch.com` from konsoleH, or move DNS to Cloudflare (free, supports flattening), copying the email (MX, SPF) records across first.
6. **Verify the email domain.** Add the TXT and CNAME records the script printed (domain, SPF, DKIM, DKIM2). When the domain shows *Verified* in the Azure portal (Email Communication Service, then Domains), run the deployment again with `-UseCustomEmailDomain`. Emails then come from `DoNotReply@shaperschurch.com`.
7. **Automatic deployments from GitHub.**
   ```powershell
   ./infra/azure/github-access.ps1
   ```
   In GitHub:
   - Settings, then Environments: create **production** and add the **variables** the script prints.
   - Settings, then Secrets and variables, then Actions (repository level): add the **secrets** `AZURE_STATIC_WEB_APPS_API_TOKEN` (website) and `AZURE_STATIC_WEB_APPS_ADMIN_TOKEN` (admin portal). Each Static Web App shows its token under *Manage deployment token*. Also add the **variable** `WEBSITE_API_URL = https://api.shaperschurch.com`.
   From then on, every merge to `main` that passes CI deploys the API and the admin portal, and the website rebuilds hourly and on publish.
8. **Go-live checklist** (below).

### DNS records (konsoleH)

| Name | Type | Value | Why |
|---|---|---|---|
| `api` | CNAME | the API's temporary address (`ca-shapers-prod-api.<…>.southafricanorth.azurecontainerapps.io`) | API |
| `asuid.api` | TXT | the verification ID from step 4 | Proves we own the name |
| `admin` | CNAME | the admin portal's address (`<…>.azurestaticapps.net`) | Admin portal |
| `www` | CNAME | the website's address (`<…>.azurestaticapps.net`) | Public website |
| `@` | TXT, CNAME ×2 | printed by the deploy script | Email domain verification, SPF and DKIM |

Leave the existing **MX** and email records alone.

## Everyday tasks

- **Deploy:** merge to `main`. To deploy by hand, open Actions, then *Deploy API*, then *Run workflow*.
- **Roll back:** pick the previous image tag (the git commit), then:
  ```powershell
  az containerapp update -g rg-shapers-prod -n ca-shapers-prod-api --image crshapersprod.azurecr.io/shapers-api:<previous sha>
  ```
  Database changes are forward-only, so a rollback past a migration needs care.
- **Logs:** Azure portal, then Application Insights `appi-shapers-prod`, then *Logs* or *Failures*. Live console:
  ```powershell
  az containerapp logs show -g rg-shapers-prod -n ca-shapers-prod-api --follow
  ```
- **Background jobs:** `https://api.shaperschurch.com/jobs` (staff with the jobs permission only). What is stuck or failed is summarised at `/api/admin/background-health`, and on the admin portal's **Background work** page.
- **App crashes:** the admin portal and the member app report crashes to `POST /api/client-errors` (open to anyone, 10 a minute per address by default: `RateLimits:ClientErrorsPerMinute`). Nothing is stored; the API logs one warning per crash, with emails, phone numbers, ids, tokens and query strings removed on the device and again on the server.
- **Change a setting:** non-secret settings go in `backend/src/Host/Shapers.Api/appsettings.Production.json`; secrets go in Key Vault (`kv-shapers-prod`), then run the deploy script again.
- **Change who gets alerts or the budget:** run the deploy script again with a different `-AlertEmail` or `-MonthlyBudget`.
- **Choose an SMS provider:** add a sender for it in the Identity module, put its key in Key Vault, and set `Sms:Provider`. Until then members see "Signing in with a code by SMS isn't available yet"; staff are unaffected.

## Alerts

The deploy script sets these up and emails them to `-AlertEmail`. Each alert also sends a "resolved" email when things recover.

| Alert | Fires when | First thing to do |
|---|---|---|
| **API: server errors** | 5 or more requests fail with a server error in 15 minutes | Application Insights, then *Failures*: look at the top exception and which page it came from. If it started after a deploy, roll back (above). |
| **API: readiness check failing** | The readiness check fails 3 or more times in 10 minutes. The API takes no traffic while it fails | Usually the database. Check the PostgreSQL server is running in the portal, then the API logs. |
| **API restarts** | The API container restarted | Check the logs just before the restart (out of memory, a crash at start-up, a failing migration). One restart after a deploy can be normal; repeated restarts are not. |
| **Database CPU** | Average above 80% for 30 minutes | The B1ms server is small and runs on CPU credits. Look for a slow query or a stuck job (`/jobs`). If it's normal load, move up a size. |
| **Database storage** | Above 80% full | Storage grows by itself, which raises the cost. Check what grew (sermon files are in Blob Storage, not here). |
| **Background work needs attention** | Messages between modules have waited more than 10 minutes or were given up on after 10 attempts, a background job failed after all its retries, or the job server stopped. The API checks every 5 minutes; the alert checks every 15 (`-BackgroundAlertMinutes`) | Open **Background work** in the admin portal to see what and where. Retry or remove failed jobs from the jobs dashboard. Messages that were given up on need a developer: the cause is usually in the API logs at the time shown. |
| **Apps: crashes reported** | The admin portal or the member app crashed 3 or more times in an hour. The apps send each crash to the API (personal details stripped), which logs a warning starting "Client error in" | Application Insights, then *Logs*: `AppTraces \| where Message has "Client error in"`. Each line says which app and version, which page (ids replaced by `:id`), the error and the first lines of the stack. If it started after a deploy, roll back. |
| **Cost budget** | 80% of the monthly budget spent, or 100% forecast | Azure portal, then *Cost Management*, then *Cost analysis* for this resource group. |

To check the email arrives: Azure portal, then *Monitor*, then *Alerts*, then *Action groups*, then `ag-shapers-prod`, then *Test*.

## Backups and restore
- **Database:** automatic, restorable to any minute in the last **14 days**. Restoring creates a *new* server, so nothing is overwritten:
  ```powershell
  az postgres flexible-server restore -g rg-shapers-prod --source-server psql-shapers-prod `
    --name psql-shapers-restore --restore-time "2026-10-05T08:00:00Z"
  ```
  Then point the `connection-string-shapers` secret at it and restart the API. Practise this once before go-live.
- **Files:** deleted blobs can be recovered for **14 days**.
- **Keys:** Key Vault keeps deleted secrets for 90 days, and they can't be purged early.

## Keys: handle with care
| Secret | If it changes |
|---|---|
| `auth-jwt-signing-key` | Everyone in the app is signed out once; they sign in again. |
| `auth-otp-hash-key` | Codes already sent stop working (they expire in minutes anyway). |
| `security-hash-key` | **Guest ticket links and other stored lookups stop matching.** Don't rotate without a plan. |
| `postgres-admin-password` | Change it on the server and in `connection-string-shapers` together. |

## Costs (rough, per month, South Africa North)
| Item | Estimate |
|---|---|
| Container Apps (0.5 vCPU, 1 GB, always on) | R300–R650 |
| PostgreSQL B1ms + 32 GB + backups | R300–R450 |
| Container Registry (Basic) | ~R90 |
| Log Analytics / Application Insights | R50–R150 |
| Storage, Key Vault, email | under R100 |
| Alerts (2 log alerts every 5 minutes, 2 every 15 minutes, 3 metric alerts; email and the budget are free) | about R80 |
| Static Web Apps (Free) ×2, private network | R0 |
| **Total** | **about R870–R1,470** |

Check the Azure pricing calculator before committing; a nonprofit grant may cover most of this.

## Go-live checklist
- [ ] First administrator signed in, authenticator set up, password changed; `admin@shapers.local` never used in production.
- [ ] Information Officer named in the privacy notice (replace the `[name]` and `[email]` placeholders), and a new notice version published.
- [ ] Service times, phone numbers and address confirmed on the website.
- [ ] Email domain verified; a test event booking email arrives and isn't marked as spam.
- [ ] A test sermon upload (audio and PDF) plays from the website.
- [ ] Database restore practised once.
- [ ] Alerts: a test from the action group (see *Alerts*) arrives at the alert email and isn't marked as spam.
- [ ] WordPress backup taken in konsoleH, and blog images copied before WordPress is switched off.
- [ ] SMS provider chosen (members can't sign in to the app until then).

## Troubleshooting
- **API won't start, "Sms:Provider … not supported":** set `Sms:Provider` to `Disabled` or a supported provider.
- **API won't start, database SSL error:** the connection string uses `SSL Mode=VerifyFull`. Check the image has CA certificates, or temporarily use `Require` (still encrypted).
- **Admin portal shows "Something went wrong" on every call:** check `Cors:Origins` includes `https://admin.shaperschurch.com`, and that the admin build had `VITE_API_URL` set.
- **Uploads fail from the admin portal:** the storage account's CORS rule must allow `https://admin.shaperschurch.com`.

## YouTube captions

YouTube only lets a channel's owner download captions, so the church creates its own Google sign-in for the platform once:

1. In the [Google Cloud console](https://console.cloud.google.com/), signed in with the church's Google account, create a project (e.g. *Shapers Church platform*) and enable the **YouTube Data API v3**.
2. **OAuth consent screen:** user type *External*, app name *Shapers Church*, support email info@shaperschurch.com. Add the scope `.../auth/youtube.force-ssl`. Then choose **Publish app** (*In production*). Left in *Testing*, Google ends the access after 7 days. Google may show an "unverified app" warning; only the channel owner sees it, and they can continue.
3. **Credentials > Create OAuth client ID:** type *Web application*, authorised redirect URI `https://api.shaperschurch.com/api/media/youtube/callback`. Pass the client ID and secret to `deploy.ps1` (they go to Key Vault).
4. In the admin portal, under **Publishing > Sermons**, the person who owns the church's YouTube channel clicks **Connect the church's channel** and signs in. From then on, sermons with a YouTube link get their captions as a transcript (a few every hour; YouTube's daily quota allows about 40).

The refresh token is stored encrypted with the API's data protection keys. **Disconnect** on the same page removes it and revokes it at Google. If those keys are ever lost, the page asks for the channel to be connected again.
