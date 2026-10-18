#!/usr/bin/env sh
# Daily PostgreSQL backup of Stockly with retention (docs/operations/backup.md).
#
# Reads the connection settings from the repository's .env (POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD,
# POSTGRES_HOST, POSTGRES_PORT); nothing is hard-coded and no secret is printed or put on a command line.
#   MODE=docker (default): pg_dump inside the compose service "postgres" (no client tools on the host).
#   MODE=local: pg_dump from PATH against POSTGRES_HOST:POSTGRES_PORT (default 127.0.0.1:5432); the password goes to
#               pg_dump through PGPASSWORD in its own environment only.
# Writes stockly-<db>-<UTC timestamp>.dump (custom format; restore with pg_restore) and deletes this script's own dumps
# older than RETENTION_DAYS.
#
# Usage: OUTPUT_DIR=/var/backups/stockly RETENTION_DAYS=14 ./scripts/backup-postgres.sh
# Cron (02:15 every night): 15 2 * * * cd /opt/stockly && ./scripts/backup-postgres.sh >> /var/log/stockly-backup.log 2>&1
set -eu

script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
repo_dir=$(dirname -- "$script_dir")
env_file=${ENV_FILE:-"$repo_dir/.env"}
output_dir=${OUTPUT_DIR:-"$HOME/stockly-backups"}
retention_days=${RETENTION_DAYS:-14}
mode=${MODE:-docker}
service=${COMPOSE_SERVICE:-postgres}

if [ ! -f "$env_file" ]; then
  echo "No .env at $env_file. Copy .env.example to .env and set the values first." >&2
  exit 1
fi

# Reads one KEY=value from .env without sourcing the file (no code execution, no export of other values).
read_env() {
  sed -n "s/^[[:space:]]*$1=//p" "$env_file" | tail -n 1 | sed 's/^"//; s/"$//'
}

db=$(read_env POSTGRES_DB)
user=$(read_env POSTGRES_USER)
password=$(read_env POSTGRES_PASSWORD)
for name in db user password; do
  eval "value=\$$name"
  if [ -z "$value" ]; then
    echo "POSTGRES_$(echo "$name" | tr '[:lower:]' '[:upper:]') is missing in $env_file." >&2
    exit 1
  fi
done

case "$retention_days" in
  ''|*[!0-9]*) echo "RETENTION_DAYS must be a whole number of days." >&2; exit 1 ;;
esac

mkdir -p "$output_dir"
stamp=$(date -u +%Y%m%dT%H%M%SZ)
target="$output_dir/stockly-$db-$stamp.dump"
partial="$target.partial"

if [ "$mode" = "docker" ]; then
  # pg_dump inside the container connects over the local socket; the password never leaves the container.
  if ! docker compose --project-directory "$repo_dir" exec -T "$service" \
      pg_dump --format=custom --no-owner --username "$user" --dbname "$db" > "$partial"; then
    rm -f "$partial"
    echo "pg_dump failed; no backup written." >&2
    exit 1
  fi
else
  host=$(read_env POSTGRES_HOST); host=${host:-127.0.0.1}
  port=$(read_env POSTGRES_PORT); port=${port:-5432}
  if ! PGPASSWORD="$password" pg_dump --format=custom --no-owner --host "$host" --port "$port" \
      --username "$user" --dbname "$db" --file "$partial"; then
    rm -f "$partial"
    echo "pg_dump failed; no backup written." >&2
    exit 1
  fi
fi

if [ ! -s "$partial" ]; then
  rm -f "$partial"
  echo "pg_dump wrote nothing; no backup written." >&2
  exit 1
fi

mv "$partial" "$target"
echo "Backup written: $target ($(du -h "$target" | cut -f1))"

# Retention: only this script's own files in the output directory.
find "$output_dir" -maxdepth 1 -type f -name "stockly-$db-*.dump" -mtime +"$retention_days" -print -exec rm -f {} \;
