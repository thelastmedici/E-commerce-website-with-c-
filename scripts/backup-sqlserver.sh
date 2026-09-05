#!/usr/bin/env bash
set -euo pipefail

: "${MSSQL_SA_PASSWORD:?Set MSSQL_SA_PASSWORD before running a backup}"

CONTAINER_NAME="${SQLSERVER_CONTAINER_NAME:-ecommerce-sqlserver}"
DATABASE_NAME="${DATABASE_NAME:-EcommerceDb}"
BACKUP_DIR="${BACKUP_DIR:-./backups}"
RETENTION_DAYS="${BACKUP_RETENTION_DAYS:-7}"
SQLCMD_PATH="${SQLCMD_PATH:-/opt/mssql-tools18/bin/sqlcmd}"

if [[ ! "$DATABASE_NAME" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "DATABASE_NAME may contain only letters, numbers, and underscores." >&2
    exit 1
fi

if [[ ! "$RETENTION_DAYS" =~ ^[0-9]+$ ]]; then
    echo "BACKUP_RETENTION_DAYS must be a non-negative integer." >&2
    exit 1
fi

mkdir -p "$BACKUP_DIR"
backup_name="${DATABASE_NAME}_$(date -u +%Y%m%dT%H%M%SZ).bak"
container_backup_path="/var/opt/mssql/backup/$backup_name"

docker exec "$CONTAINER_NAME" mkdir -p /var/opt/mssql/backup

docker exec "$CONTAINER_NAME" "$SQLCMD_PATH" \
    -S localhost \
    -U sa \
    -P "$MSSQL_SA_PASSWORD" \
    -C \
    -Q "BACKUP DATABASE [$DATABASE_NAME] TO DISK = N'$container_backup_path' WITH INIT, COMPRESSION, CHECKSUM"

docker cp "$CONTAINER_NAME:$container_backup_path" "$BACKUP_DIR/$backup_name"
docker exec "$CONTAINER_NAME" rm -f "$container_backup_path"

find "$BACKUP_DIR" -maxdepth 1 -type f -name "${DATABASE_NAME}_*.bak" -mtime "+$RETENTION_DAYS" -delete
echo "Created $BACKUP_DIR/$backup_name"
