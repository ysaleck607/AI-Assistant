#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

cd "$ROOT_DIR"

if [[ -n "${SQL_SERVER_PASSWORD:-}"
    && -n "${SQLPAD_ADMIN_EMAIL:-}"
    && -n "${SQLPAD_ADMIN_PASSWORD:-}" ]]; then
    COMPOSE=(docker compose)
elif [[ -f "$ROOT_DIR/.env.database" ]]; then
    set -a
    source "$ROOT_DIR/.env.database"
    set +a
    COMPOSE=(docker compose --env-file .env.database)
else
    echo "La configuration SQL locale est absente." >&2
    echo "Configure .env.database ou fournis SQL_SERVER_PASSWORD, SQLPAD_ADMIN_EMAIL et SQLPAD_ADMIN_PASSWORD." >&2
    exit 1
fi

if [[ -z "${SQL_SERVER_PASSWORD:-}" ]]; then
    echo "SQL_SERVER_PASSWORD est requis pour préparer AssistantCoreDb." >&2
    exit 1
fi

"${COMPOSE[@]}" up --build -d sqlserver sqlpad

echo "Préparation de la base AssistantCoreDb..."
for attempt in {1..60}; do
    if docker exec assistantcore-sqlserver \
        //opt/mssql-tools18/bin/sqlcmd \
        -C -S localhost -U sa -P "$SQL_SERVER_PASSWORD" \
        -Q "IF DB_ID(N'AssistantCoreDb') IS NULL CREATE DATABASE [AssistantCoreDb];" \
        >/dev/null 2>&1; then
        break
    fi

    if [[ "$attempt" -eq 60 ]]; then
        echo "SQL Server n'est pas disponible après 60 secondes." >&2
        exit 1
    fi
    sleep 1
done

if [[ "${ASSISTANTCORE_SKIP_DATABASE_MIGRATIONS:-false}" != "true" ]]; then
    "${COMPOSE[@]}" run --rm --build --no-TTY flyway
fi
