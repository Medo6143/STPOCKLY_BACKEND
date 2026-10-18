# Backup and restore

Sprint 6. What must survive a lost server, how often it is copied, and how a restore is proven.

## 1. Targets

| | Target | Why |
|---|---|---|
| **RPO** (data that may be lost) | ≤ 24 h for PostgreSQL (nightly dump), ≤ 24 h for images (nightly mirror) | Shops can re-enter at most one day of sales and re-scan one day of invoices from paper |
| **RTO** (time to be back) | ≤ 4 h | New server + `docker compose up -d` + restore of a dump of a few hundred MB fits easily |

Tighter targets later need PostgreSQL WAL archiving (point-in-time recovery, e.g. pgBackRest or a managed database)
and MinIO bucket replication; the procedure below stays the base.

## 2. What is backed up

| Data | Where | How | Retention |
|---|---|---|---|
| Everything in PostgreSQL: shops, users, catalog, stock ledger, invoices, sales, AI usage, audit log, and the Hangfire schema | database `POSTGRES_DB` | `scripts/backup-postgres.ps1` / `.sh`: `pg_dump --format=custom` nightly | 14 daily dumps on the server (`-RetentionDays` / `RETENTION_DAYS`), plus a weekly copy kept off-site for 8 weeks |
| Invoice images | MinIO bucket `MINIO_BUCKET` | `mc mirror` nightly to a second location | the images expire after 90 days anyway (`docs/operations/image-lifecycle.md`) |
| Configuration and secrets | `.env` on the server | stored in the team's password manager, never in git | — |

**Not backed up:** Redis. It is a cache only (`docs/design/redis.md`): read cache, AI result cache, invoice status
snapshots, scan rate-limit and daily-quota counters. After a restore it starts empty; reads go to PostgreSQL, the daily
quota falls back to counting today's invoices in PostgreSQL, and the catalog version keys are recreated on first use.
Hangfire jobs live in PostgreSQL (schema `hangfire`) and come back with the dump.

## 3. Nightly PostgreSQL dump

The scripts read the connection settings from `.env` (`POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, and for
`Local` mode `POSTGRES_HOST`/`POSTGRES_PORT`); nothing is hard-coded and the password is never printed or passed on a
command line. Default mode runs `pg_dump` inside the compose `postgres` container. The file is written as `.partial`
and renamed only when `pg_dump` succeeded and wrote something; then dumps of this database older than the retention are
deleted.

```bash
# Linux server, cron every night at 02:15 server time
15 2 * * * cd /opt/stockly && OUTPUT_DIR=/var/backups/stockly RETENTION_DAYS=14 ./scripts/backup-postgres.sh >> /var/log/stockly-backup.log 2>&1
```

```powershell
# Windows (Task Scheduler, daily)
pwsh -File D:\stockly\scripts\backup-postgres.ps1 -OutputDirectory D:\backups\stockly -RetentionDays 14
```

Copy the newest dump off the server every day (another disk or region; e.g. `rclone copy` or `mc cp` to a second
bucket with its own credentials). A backup that lives only next to the database is not a backup.

## 4. Images: `mc mirror`

```bash
mc alias set stockly  http://127.0.0.1:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD"
mc alias set offsite  https://backup.example.net "$OFFSITE_ACCESS_KEY" "$OFFSITE_SECRET_KEY"
mc mirror --overwrite --remove stockly/stockly-invoices offsite/stockly-invoices-backup
```

`--remove` keeps the copy in step with the 90-day expiry. For a near-zero image RPO use MinIO bucket replication
(`mc replicate add`) to the second site instead of the nightly mirror (needs versioning on both buckets).

## 5. Restore drill (do it once before the first shop, then every quarter)

1. Start an empty stack on a scratch machine: `cp .env.example .env` (fill in), `docker compose up -d postgres redis minio minio-init`.
2. Restore the database (the dump contains the schema, the data, the `hangfire` schema and the `stockly_latin_key` function):
   ```bash
   docker compose exec -T postgres pg_restore --clean --if-exists --no-owner \
     --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" < stockly-stockly-20261018T021500Z.dump
   ```
3. Check: `dotnet ef migrations list` against the restored database shows every migration applied (no pending one);
   row counts of `shops`, `products`, `stock_movements`, `invoices`, `sales` match the source's last report.
4. Restore the images: `mc mirror offsite/stockly-invoices-backup stockly/stockly-invoices`.
5. Start the Api; `GET /health/ready` is `Healthy`; sign in as a shop owner, open the product list and an invoice; sign
   in to the admin and open AD18 for a recent invoice (the image URL opens).
6. Record the time from step 1 to step 5 (must stay under the RTO) and the dump's age (RPO) in the operations log.

The scripts were checked on 2026-10-18 against a local throw-away database: the dump was written and
`pg_restore --list` read 33 table-data entries and the `stockly_latin_key` function from it.
