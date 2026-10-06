# Temporary demo deployment

A low-cost, temporary copy of the whole platform for showing the church, **with demo data only**. Production is the Azure setup in [deployment.md](deployment.md).

```
Website       Azure Static Web Apps (free)            https://<site>.azurestaticapps.net
Admin + API   Google Compute Engine e2-micro (free)   https://<vm-ip>.sslip.io
                Caddy (automatic HTTPS) -> API container (Demo environment)
Database      Supabase (free), US East
Email         Azure Communication Services (fractions of a cent per email)
```

- The admin portal and the API share one address, so the staff sign-in cookie works.
- Uploaded files and the cookie keys live on the VM's disk (Docker volume `api-data`).
- `Demo` environment (`appsettings.Demo.json`): files on local disk, SMS disabled (members can't sign in to the app), sample sermons and a Sunday livestream on first start.

## Deploy or update

```powershell
./infra/demo/deploy-demo.ps1 -Project <gcp project> -AdminEmail <first admin email> `
  -DatabaseConnectionFile <file with the Supabase connection string> `
  -WebsiteOrigin https://<site>.azurestaticapps.net `
  -EmailConnectionFile <file with the email connection string> -EmailFrom DoNotReply@<domain>
```

The script is safe to run again. Keys are kept in `/opt/shapers/.env` on the VM; the first administrator's password is printed once.

Supabase connection string: use the **session pooler** (IPv4), e.g.
`Host=aws-0-us-east-1.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.<project ref>;Password=<password>;SSL Mode=Require;Maximum Pool Size=10`

Then rebuild the website against the demo API: build `web` with `API_URL` and `PUBLIC_API_URL` set to the VM address, and upload `web/dist` to the Static Web App.

## Limits (it's a demo)
- **Not for real member data:** the database is outside South Africa, and the free tier has no backups.
- Supabase may pause a free project after a week without activity; resume it from the Supabase dashboard.
- Google's free tier covers one e2-micro VM in a US region. The public IP address may carry a small charge.
- Logs: `gcloud compute ssh shapers-demo --zone us-east1-b --command "sudo docker logs shapers-demo-api-1 --tail 100"`.

## Remove it
```powershell
gcloud compute instances delete shapers-demo --zone us-east1-b --project <gcp project>
gcloud compute firewall-rules delete shapers-demo-web --project <gcp project>
```
Then delete the Supabase project from its dashboard.
