# Shapers Church Platform

The digital platform for [Shapers Church](https://shaperschurch.com), Johannesburg. One backend serves three clients:

- **Admin portal** (`admin/`): where staff run the church: people, households, access, and later giving, events, sermons, rosters and check-in.
- **Member app** (`mobile/`): iOS and Android, for watching, giving, registering, groups, prayer and serving.
- **Public website**: planned for V1.

See [docs/overview.md](docs/overview.md) for the vision, modules and roadmap, and [docs/decisions/](docs/decisions/) for architecture decisions.

## Repository

| Path | What |
|---|---|
| `backend/` | ASP.NET Core (.NET 10) modular monolith: `src/Modules/{Church,People,Identity}`, shared `BuildingBlocks`, and the `Host/Shapers.Api` composition root |
| `admin/` | Admin portal (React + Vite + TypeScript) |
| `mobile/` | Member app (Expo + Expo Router, native tabs, Liquid Glass) |
| `packages/tokens` | Midnight and Rose theme tokens, generated from `design/tokens/tokens.json` |
| `packages/api-client` | Typed API client, generated from the backend's OpenAPI document |
| `design/` | Mockup (`shapers-glass.html`), token source and brand assets |
| `infra/` | Local development services (PostgreSQL) |
| `docs/` | Overview, Phase 0 plan, architecture decision records |

## Prerequisites

- .NET SDK 10.0.4xx (pinned in `backend/global.json`)
- Node.js 22+ and pnpm 10 (`corepack enable`)
- Docker Desktop (local database and integration tests)
- For the app: Expo Go or a development build; Xcode 26 to see Liquid Glass on an iOS 26 simulator

## Run it locally

```bash
# 1. Database
docker compose -f infra/docker-compose.yml up -d

# 2. API on http://localhost:5080 (migrates and seeds on start)
dotnet run --project backend/src/Host/Shapers.Api
```

On first start in Development the API creates an administrator (`admin@shapers.local`) and prints a one-time password in the console. API reference: http://localhost:5080/docs. Background jobs: http://localhost:5080/jobs.

To keep sessions across API restarts, set persistent development keys once:

```bash
cd backend/src/Host/Shapers.Api
dotnet user-secrets set "Auth:Jwt:SigningKey" "$(openssl rand -base64 32)"
dotnet user-secrets set "Auth:Otp:HashKey" "$(openssl rand -base64 32)"
```

```bash
# 3. Web and mobile
pnpm install
pnpm admin     # admin portal on http://localhost:5173 (proxies /api to the API)
pnpm mobile    # Expo dev server
```

In Development, sign-in codes for the app are written to the API console instead of being sent by SMS.

## Everyday commands

| Command | Does |
|---|---|
| `dotnet build backend/Shapers.slnx` | Build the backend; also regenerates `packages/api-client/openapi.json` |
| `cd backend && dotnet test` | Unit, architecture and integration tests (integration tests need Docker) |
| `pnpm api:generate` | Regenerate the TypeScript API types after backend changes |
| `pnpm tokens` | Regenerate theme tokens after editing `design/tokens/tokens.json` |
| `pnpm typecheck` / `pnpm lint` / `pnpm test` | Checks across the JavaScript workspace |
| `dotnet ef migrations add <Name> --project backend/src/Modules/<Module>/Shapers.<Module>.Infrastructure --startup-project backend/src/Host/Shapers.Api --context <Module>DbContext --output-dir Migrations` | Add a migration (run `dotnet tool restore` first) |

## Working rules

- **Modules talk through Contracts only.** Architecture tests fail the build otherwise.
- **Ask for permissions, never roles.** Use `IAuthorizer` with a permission and a scope.
- **No hard-coded colours** in app code. Use the theme tokens. CI checks this.
- **No secrets in the repo.** Use `dotnet user-secrets` locally and a key vault in hosted environments.
- **Commits** follow conventional commits (`feat(people): add household model`). Work happens on `development`; `main` is updated by pull request.
