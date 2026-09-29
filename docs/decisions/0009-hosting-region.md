# 0009. Host in Azure South Africa North

Status: Accepted (2026-09-29)

## Context
Church records include special personal information under POPIA. Sending personal information outside South Africa brings extra conditions (POPIA section 72). Most members are in Johannesburg.

## Decision
Host data-bearing services in **Azure South Africa North** (Johannesburg): the database, file storage, secrets, the API and background jobs. Check each new Azure service's regional availability before adopting it.

Availability checked on 2026-09-29 with `az provider show`. All of these are available in South Africa North: Container Apps, App Service, Database for PostgreSQL flexible server, Storage, Key Vault, Container Registry, Application Insights, Log Analytics, Notification Hubs and Azure AI services.

Exceptions to note:
- **Azure Communication Services** (email, SMS) is a global resource. With the **Africa** data location its stored data stays in Africa, but messages may be processed in transit elsewhere. Record this in the privacy notice.
- **Azure OpenAI models** are deployed per model and region. Check that the models we need can be deployed in South Africa North before V3.
- **CDN and edge** (Cloudflare) cache only public content: published sermon audio, artwork and public pages. Never personal data.

## Consequences
- Simpler POPIA position, and low latency for members.
- A few newer Azure features reach this region later than others. Each new service needs the availability check above.
