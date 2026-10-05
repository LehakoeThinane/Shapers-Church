# 0015. Production deployment on Azure

Status: Accepted (2026-10-05)

## Context
ADR 0009 chose Azure South Africa North. We now need the concrete setup for a team of four with no dedicated operations person. Alternatives considered: the church's existing xneelo (konsoleH) shared hosting, which runs only PHP and MySQL, xneelo Cloud and other self-managed servers, and Google Cloud. Shared hosting cannot run the API, database or background jobs. Self-managed servers would make someone on the team responsible for patching, backups and certificates. Google Cloud would need new storage and email adapters.

## Decision
- **API:** one container on **Azure Container Apps** (Consumption workload profile), always on with exactly one replica, so recurring jobs, the outbox and the live chat hub keep running. The image is built in Azure Container Registry.
- **Database:** **Azure Database for PostgreSQL Flexible Server** 17, Burstable B1ms, 14-day backups. It is reachable only from the API's private network (no public access). The `vector` extension is allow-listed for later.
- **Secrets:** **Key Vault** (RBAC), read by the API through a user-assigned managed identity. Keys are generated on first deployment and reused afterwards. Rotating the hash keys invalidates stored codes and guest links, so they are not rotated casually.
- **Files:** Blob Storage; the `media` container allows public reads of published files, and uploads use short-lived signed URLs.
- **Email:** Communication Services (Africa data location). It sends from the Azure-managed domain until `shaperschurch.com` is verified, then from `DoNotReply@shaperschurch.com`.
- **Monitoring:** Log Analytics and Application Insights, fed by the Container Apps OpenTelemetry agent.
- **Websites:** the public website and the admin portal on **Azure Static Web Apps (Free)**. konsoleH keeps DNS and email only. The admin portal (`admin.shaperschurch.com`) calls the API (`api.shaperschurch.com`) cross-origin; the two are the same site, so the `SameSite=Strict` staff cookie still works.
- **One environment (production)** for now; staging can be added later by deploying the same template under another name.
- **Infrastructure as code** in `infra/azure/main.bicep`, deployed by `infra/azure/deploy.ps1`. Deployments after CI passes on `main` use GitHub OpenID Connect with no stored Azure credentials.
- **SMS** is `Disabled` until a provider is chosen: the API starts, and member SMS sign-in returns a clear message. `Log` and unknown provider names refuse to start outside development, so sign-in codes can never be written to production logs.

## Consequences
- No servers to patch; backups, certificates and scaling are Azure's job.
- Exactly one API replica. Scaling out first needs a SignalR backplane (Azure SignalR Service or Redis) and a check that recurring jobs run once.
- The API trusts the platform ingress's forwarded headers (`ForwardedHeaders:TrustPlatformProxy`). Putting another proxy such as Cloudflare in front of the API means revisiting the forward limit.
- Estimated cost is in `docs/deployment.md`. A Microsoft nonprofit grant may cover most of it.
