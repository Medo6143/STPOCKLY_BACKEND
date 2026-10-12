# stockly-backend

API and admin for Stockly, an AI inventory product for small shops in Egypt. Photograph a supplier invoice, review what the AI extracted, confirm, and stock updates. Arabic first, English too.

Read [`AGENTS.md`](AGENTS.md) before your first change. Design docs live in [`docs/`](docs/). The Flutter app and brand assets live in the `stockly-mobile` repository and use this API through [`docs/api/openapi.json`](docs/api/openapi.json) ([ADR-0011](docs/adr/0011-polyrepo-backend-and-mobile.md)).

## Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0.x (pinned in [`global.json`](global.json)) |
| Docker Desktop | with Compose v2 |

## Run locally

### 1. Infrastructure (Docker)

```bash
cp .env.example .env          # then replace every change-me value
docker compose up -d          # postgres, redis, minio (+ minio-init one-shot)
docker compose ps -a          # postgres/redis/minio "healthy", minio-init "Exited (0)"
```

| Service | URL (127.0.0.1 only) | Credentials from `.env` |
|---|---|---|
| PostgreSQL 16 | `localhost:5432` | `POSTGRES_USER` / `POSTGRES_PASSWORD` |
| Redis 7 (cache only) | `localhost:6379` | `REDIS_PASSWORD` |
| MinIO S3 API | http://localhost:9000 | API: `STORAGE_ACCESS_KEY` / `STORAGE_SECRET_KEY` |
| MinIO console | http://localhost:9001 | `MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` |
| pgAdmin (dev profile) | http://localhost:5050 | `PGADMIN_DEFAULT_EMAIL` / `PGADMIN_DEFAULT_PASSWORD` |

- pgAdmin: `docker compose --profile dev up -d`.
- Port already taken (for example a local PostgreSQL)? Set `POSTGRES_PORT`, `REDIS_PORT`, `MINIO_API_PORT`, `MINIO_CONSOLE_PORT` or `PGADMIN_PORT` in `.env`, and update the matching `ConnectionStrings__*` / `Storage__Endpoint` values.
- Missing variable? Compose stops with `required variable X is missing a value`. Compare your `.env` with `.env.example`.
- Reset everything (deletes all local data): `docker compose --profile dev down -v`, then `docker compose up -d`.

### 2. Backend (from the repo root)

```bash
dotnet tool restore
dotnet build -warnaserror
dotnet test
dotnet run --project src/Stockly.Api
```

The API runs on the host and reads the `ConnectionStrings__*`, `Storage__*`, `Jwt__*` and `OpenRouter__*` values (wired up in Sprint 1, step 1.4). The full command list (migrations, admin) is in [`AGENTS.md` §3](AGENTS.md#3-commands).

## Repository layout

| Path | What |
|---|---|
| `Stockly.sln`, `src/`, `tests/` | .NET solution: Domain, Contracts, Application, Infrastructure, Api + tests (Admin from Sprint 5) |
| `docker-compose.yml`, `.env.example` | Local Postgres, Redis, MinIO, pgAdmin |
| `docs/` | Roadmap, ERD, Redis design, ADRs, feature docs, sprint logs, API contract |

Layer rules are enforced by `tests/Stockly.ArchitectureTests`. See [`docs/architecture/structure.md`](docs/architecture/structure.md).
