# Stockly

AI inventory for small shops in Egypt. Photograph a supplier invoice, review what the AI extracted, confirm, and stock updates. Arabic first, English too.

Read [`AGENTS.md`](AGENTS.md) before your first change. Design docs live in [`docs/`](docs/), brand assets in [`brand/`](brand/).

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 8.0.x (pinned in [`global.json`](global.json)) |
| Docker Desktop | with Compose v2 |
| Flutter | stable (from Sprint 3) |

## Run in 5 minutes

```bash
# 1. infrastructure: postgres, redis, minio (added in Sprint 1, step 1.2)
cp .env.example .env
docker compose up -d

# 2. backend
cd backend
dotnet build -warnaserror
dotnet test
dotnet run --project src/Stockly.Api
```

The full command list (migrations, admin, mobile) is in [`AGENTS.md` §3](AGENTS.md#3-commands).

## Repository layout

| Path | What |
|---|---|
| `backend/` | .NET 8 solution: Domain, Contracts, Application, Infrastructure, Api + tests |
| `mobile/` | Flutter app (from Sprint 3) |
| `docs/` | Roadmap, ERD, Redis design, ADRs, feature docs |
| `brand/` | Logo, splash spec, color and type tokens |

Layer rules are enforced by `backend/tests/Stockly.ArchitectureTests`. See [`docs/architecture/structure.md`](docs/architecture/structure.md).
