# Stockly — Project Structure

Status: **Draft for review**

## 1. Repository root

```
stockly/
├── AGENTS.md                     # rules for every contributor (human or AI)
├── README.md                     # how to run in 5 minutes
├── docker-compose.yml            # postgres, redis, minio, pgadmin (dev profile)
├── .env.example                  # placeholders only, .env is git-ignored
├── .gitignore
├── .gitattributes                # LF everywhere, CRLF for *.sln and *.cmd
├── .editorconfig
├── global.json                   # .NET SDK 8.0.x, rollForward latestFeature
├── .github/
│   ├── workflows/
│   │   ├── backend.yml           # build, test, coverage (paths: backend/**)
│   │   ├── mobile.yml            # analyze, test (paths: mobile/**)
│   │   └── admin.yml             # build, bUnit tests
│   └── pull_request_template.md  # = checklist from AGENTS.md §11
├── brand/                        # logo SVGs, splash spec, tokens
├── docs/
│   ├── roadmap.md
│   ├── erd.md
│   ├── architecture/structure.md
│   ├── design/redis.md
│   ├── design/backend.md         # next planning step
│   ├── design/frontend.md        # next planning step
│   ├── adr/NNNN-*.md            # 0001–0009
│   └── features/_TEMPLATE.md, <feature>.md
├── backend/
└── mobile/
```

## 2. Backend

```
backend/
├── Stockly.sln
├── Directory.Build.props         # net8.0, nullable, TreatWarningsAsErrors, analyzers
├── Directory.Packages.props      # central package versions
├── src/
│   ├── Stockly.Domain/
│   │   ├── Common/               # Entity, Result<T>, Error, ErrorType, Guard
│   │   ├── Shops/                # Shop, ShopMember, ShopRole, ShopType, Plan, ShopSubscription
│   │   ├── Products/             # Product, ProductUnit, ProductAlias
│   │   ├── Suppliers/            # Supplier
│   │   ├── Invoices/             # Invoice, InvoiceItem, InvoiceStatus, InvoiceWarning, TotalsTolerancePolicy
│   │   ├── Sales/                # Sale, SaleItem
│   │   ├── Stock/                # StockMovement, MovementReason
│   │   ├── Platform/             # AiUsageRecord, AdminAuditLog
│   │   └── Text/                 # ArabicTextNormalizer
│   │
│   ├── Stockly.Contracts/        # shared with Stockly.Admin
│   │   ├── Auth/  Products/  Invoices/  Sales/  Dashboard/  Admin/
│   │   └── Common/PagedResponse.cs
│   │
│   ├── Stockly.Application/
│   │   ├── Abstractions/
│   │   │   ├── Persistence/      # IProductRepository, IInvoiceRepository, ISaleRepository,
│   │   │   │                     # ISupplierRepository, IUnitOfWork
│   │   │   ├── Stock/            # IStockService
│   │   │   ├── Ai/               # IAiInvoiceExtractor, AiExtractionResult
│   │   │   ├── Caching/          # ICacheService, IAiResultCache, IInvoiceStatusCache
│   │   │   ├── Limits/           # IScanRateLimiter, IScanQuotaCounter
│   │   │   ├── Storage/          # IFileStorage
│   │   │   ├── Jobs/             # IBackgroundJobScheduler
│   │   │   ├── Identity/         # ICurrentUser, ICurrentTenant, ITokenService
│   │   │   └── Time/             # IClock
│   │   ├── Behaviors/            # Validation, Logging, Transaction (commands only)
│   │   ├── Features/
│   │   │   ├── Auth/             # Register, Login, Refresh, Logout, SwitchShop
│   │   │   ├── Shops/            # GetMyShop, InviteMember, RemoveMember
│   │   │   ├── Products/         # CreateProduct, UpdateProduct, ArchiveProduct,
│   │   │   │                     # GetProducts, GetProductById, GetLowStock, AdjustStock, StockCount
│   │   │   ├── Suppliers/        # GetSuppliers, CreateSupplier
│   │   │   ├── Invoices/
│   │   │   │   ├── ScanInvoice/          # 202 + enqueue job
│   │   │   │   ├── ProcessInvoice/       # job handler: storage → cache → AI → validate → save
│   │   │   │   ├── GetInvoice/  GetInvoices/
│   │   │   │   ├── UpdateInvoiceItems/
│   │   │   │   ├── ConfirmInvoice/
│   │   │   │   ├── Matching/             # IProductMatcher, alias + trigram matchers
│   │   │   │   └── Validation/           # IInvoiceValidationRule + one class per rule
│   │   │   ├── Sales/            # CreateSale, GetSales
│   │   │   ├── Dashboard/        # GetDashboardSummary, GetExpiringSoon
│   │   │   └── Admin/            # the ONLY place allowed IgnoreQueryFilters()
│   │   │       ├── Shops/  Plans/  AiUsage/  FailedInvoices/  AuditLog/
│   │   └── DependencyInjection.cs
│   │
│   ├── Stockly.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── StocklyDbContext.cs
│   │   │   ├── Configurations/   # one IEntityTypeConfiguration per entity
│   │   │   ├── Interceptors/     # TenantStampInterceptor, AuditTimestampsInterceptor
│   │   │   ├── Repositories/
│   │   │   ├── Stock/StockService.cs
│   │   │   └── Migrations/
│   │   ├── Identity/             # Identity setup, JwtTokenService, refresh tokens
│   │   ├── Ai/OpenRouter/        # OpenRouterInvoiceExtractor, OpenRouterOptions, Prompts/, Dtos/
│   │   ├── Caching/              # RedisCacheService, RedisAiResultCache, RedisInvoiceStatusCache,
│   │   │                         # RedisScanLimiter, CacheKeys (single source of key formats)
│   │   ├── Storage/              # S3FileStorage (MinIO locally)
│   │   ├── Jobs/                 # HangfireJobScheduler, ProcessInvoiceJob
│   │   ├── Time/SystemClock.cs
│   │   └── DependencyInjection.cs
│   │
│   ├── Stockly.Api/
│   │   ├── Controllers/          # Auth, Products, Invoices, Sales, Dashboard, Admin/*
│   │   ├── Middleware/           # ExceptionHandling (ProblemDetails), CorrelationId
│   │   ├── Identity/             # CurrentUser/CurrentTenant from claims
│   │   ├── RateLimiting/
│   │   ├── Extensions/           # ResultExtensions: Result → IActionResult
│   │   ├── Program.cs
│   │   └── appsettings.json      # no secrets
│   │
│   └── Stockly.Admin/            # Blazor WASM + MudBlazor
│       ├── Pages/                # Login, Overview, Shops, ShopDetail, Plans, AiUsage, FailedScans, AuditLog
│       ├── Components/
│       ├── Services/             # typed API clients using Stockly.Contracts
│       ├── Auth/                 # JWT AuthenticationStateProvider
│       └── wwwroot/
│
└── tests/
    ├── Stockly.Domain.Tests/
    ├── Stockly.Application.Tests/        # handlers with NSubstitute fakes
    ├── Stockly.Api.IntegrationTests/     # WebApplicationFactory + Testcontainers (pg, redis, minio)
    ├── Stockly.ArchitectureTests/        # NetArchTest: layer rules, tenant filter, IgnoreQueryFilters
    ├── Stockly.Infrastructure.Tests/     # Testcontainers Postgres: migration, tenant isolation, constraints, concurrency
    └── Stockly.Admin.Tests/              # bUnit
```

> `Stockly.Contracts`, `Stockly.Admin`, `Stockly.ArchitectureTests` and `Stockly.Admin.Tests` are additions to the original prompt (ADR-0005).

> Layer rules live in `backend/tests/Stockly.ArchitectureTests`: `ProjectReferenceTests` checks the references declared in every `src/*.csproj` against the exact table in `AGENTS.md` §4; `LayerDependencyTests` (NetArchTest) checks compiled type dependencies; `ControllerSignatureTests` keeps Domain types out of controller signatures. Domain's only allowed package is `UUIDNext` (ADR-0009).

## 3. Mobile (Flutter)

```
mobile/
├── pubspec.yaml
├── analysis_options.yaml         # very_good_analysis
├── l10n.yaml
├── lib/
│   ├── main.dart                 # ProviderScope + bootstrap
│   ├── app.dart                  # MaterialApp.router, theme, locale
│   ├── l10n/                     # app_ar.arb, app_en.arb (generated code ignored)
│   ├── core/
│   │   ├── config/               # env (API base URL via --dart-define)
│   │   ├── network/              # dio_client, auth_interceptor, refresh_interceptor, error_mapper
│   │   ├── error/                # Failure (sealed), Result
│   │   ├── theme/                # tokens.dart (from brand/), app_theme.dart, stockly_colors.dart (ThemeExtension)
│   │   ├── router/               # go_router, auth redirect, route names
│   │   ├── storage/              # secure_token_storage, preferences (language, theme)
│   │   ├── offline/              # drift database, pending_scans table, scan_sync_worker
│   │   ├── media/                # image picker + compression (max 2000px, ~80% JPEG)
│   │   └── widgets/              # AsyncStateView, EmptyState, ErrorState, MoneyText, QuantityText
│   └── features/
│       ├── splash/presentation/              # animated splash (brand spec)
│       ├── auth/
│       │   ├── data/        datasources/ models/ repositories/
│       │   ├── domain/      entities/ repositories/ usecases/
│       │   └── presentation/ providers/ screens/ widgets/
│       ├── products/        (same 3 layers)  # list, detail, add/edit, units, adjust
│       ├── invoices/        (same 3 layers)  # capture, queue, processing, review, confirm, history
│       ├── sales/           (same 3 layers)  # quick sale, barcode lookup
│       ├── dashboard/       (same 3 layers)  # summary, low stock, expiring soon
│       └── settings/        (same 3 layers)  # language, theme, shop members, logout
└── test/                     # mirrors lib/
```

## 4. Docker services

Defined in `docker-compose.yml` (project `stockly`, one network `stockly`). Images are pinned by tag **and** sha256 digest in the compose file; bump both together. Every port is bound to `127.0.0.1` only, and every credential comes from `.env` (`${VAR:?}`, compose refuses to start without it).

| Service | Image (tag; digest in compose) | Host port | Volume | Profile | Health check |
|---|---|---|---|---|---|
| postgres | `postgres:16.15-alpine` | 5432 | `pg_data` | default | `pg_isready` |
| redis | `redis:7.4.11-alpine` | 6379 | none (cache only: `--save ""`, `--appendonly no`, 256 MB, `volatile-lru`, password) | default | `redis-cli ping` |
| minio | `pgsty/minio:RELEASE.2026-08-04T00-00-00Z` (community build, dev/test only, ADR-0008) | 9000 API / 9001 console | `minio_data` | default | `mc ready local` |
| minio-init | `pgsty/mc:RELEASE.2026-09-16T00-00-00Z` | none | none | default | none (one-shot, exits 0) |
| pgadmin | `dpage/pgadmin4:9.18` | 5050 | none | dev | `wget /misc/ping` |

`minio-init` waits for MinIO to be healthy, then creates the private bucket `MINIO_BUCKET` and the least-privilege API user (`STORAGE_ACCESS_KEY`, policy `stockly-api`: Get/Put/DeleteObject on the bucket's objects, ListBucket on the bucket). Re-running it is harmless.

Host ports can be moved with `POSTGRES_PORT`, `REDIS_PORT`, `MINIO_API_PORT`, `MINIO_CONSOLE_PORT` and `PGADMIN_PORT` in `.env` when a port is already taken.

In development the API runs on the host with `dotnet run` and connects to these services through the `127.0.0.1` ports. Containerizing the API is out of scope until the release sprint.
