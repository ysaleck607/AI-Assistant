#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_PROJECT="$ROOT_DIR/AssistantCore.Service"
WORKER_PROJECT="$ROOT_DIR/AssistantCore.Ingestion.Worker"
LOCAL_CONFIGURATION="$SERVICE_PROJECT/appsettings.Local.json"
LOCAL_DIRECTORY="$ROOT_DIR/.local"
WIREMOCK_CERTIFICATE="$LOCAL_DIRECTORY/wiremock.pfx"
API_URL="https://localhost:7292"
WIREMOCK_URL="https://localhost:9443"
API_PID=""
WORKER_PID=""

cleanup() {
    if [[ -n "$WORKER_PID" ]] && kill -0 "$WORKER_PID" 2>/dev/null; then
        kill "$WORKER_PID"
    fi
    if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
        kill "$API_PID"
    fi
}

trap cleanup EXIT INT TERM

for command_name in docker dotnet curl jq openssl; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "Commande requise introuvable: $command_name" >&2
        exit 1
    fi
done

mkdir -p "$LOCAL_DIRECTORY"

if [[ ! -f "$WIREMOCK_CERTIFICATE" ]]; then
    echo "Création du certificat HTTPS local de WireMock..."
    dotnet dev-certs https --check --trust
    certificate_exported=false
    for _ in {1..3}; do
        if dotnet dev-certs https \
            --export-path "$WIREMOCK_CERTIFICATE" \
            --password local-wiremock; then
            certificate_exported=true
            break
        fi
        sleep 1
    done
    if [[ "$certificate_exported" != true ]]; then
        echo "Le certificat HTTPS local n'a pas pu être exporté." >&2
        exit 1
    fi
fi

export SQL_SERVER_PASSWORD
export SQLPAD_ADMIN_EMAIL
export SQLPAD_ADMIN_PASSWORD
export SQLPAD_DATABASE="AssistantCoreLocalDb"
SQL_SERVER_PASSWORD="$(jq -er '.DatabaseStack.SqlServerPassword' "$LOCAL_CONFIGURATION")"
SQLPAD_ADMIN_EMAIL="$(jq -er '.DatabaseStack.SqlPadAdminEmail' "$LOCAL_CONFIGURATION")"
SQLPAD_ADMIN_PASSWORD="$(jq -er '.DatabaseStack.SqlPadAdminPassword' "$LOCAL_CONFIGURATION")"

cd "$ROOT_DIR"

echo "[1/7] Démarrage de SQL Server et préparation de la base locale isolée..."
ASSISTANTCORE_SKIP_DATABASE_MIGRATIONS=true bash scripts/start-database-stack.sh

# Les chemins destines au conteneur sont prefixes de deux barres obliques.
# Git Bash reecrit tout argument ressemblant a un chemin absolu POSIX avant de le
# passer a docker.exe, qui est un binaire Windows : /opt/... y deviendrait
# C:/Program Files/Git/opt/... et le conteneur ne le trouverait pas. La forme
# //opt/... traverse MSYS intacte et reste un chemin valide sous Linux et macOS.

docker exec -i assistantcore-sqlserver \
    //opt/mssql-tools18/bin/sqlcmd \
    -C -S localhost -U sa -P "$SQL_SERVER_PASSWORD" \
    < test-support/local/reset-local-database.sql

# Les migrations versionnees (V*) doivent s'executer dans l'ordre avant les
# migrations repetables (R__*, ex. provisioning des utilisateurs geres), qui
# peuvent presupposer que la base existe deja. Un tri alphabetique brut du
# dossier ferait passer R__ avant V01_000 (creation de la base) et echouerait.
#
# Les placeholders Flyway (${API_CLIENT_ID}, etc.) ne sont normalement
# substitues que par le conteneur Flyway reel (voir les valeurs par defaut
# dans AssistantCore.Repository/Database/Flyway/Dockerfile). Cette relecture
# manuelle doit reproduire les memes valeurs desactivees pour que le bloc de
# provisioning des identites managees se saute comme en local/CI.
for migration_file in AssistantCore.Repository/Database/Flyway/sql/V*.sql AssistantCore.Repository/Database/Flyway/sql/R__*.sql; do
    sed \
        -e 's/AssistantCoreDb/AssistantCoreLocalDb/g' \
        -e 's/\${API_IDENTITY_NAME}/disabled-api/g' \
        -e 's/\${API_CLIENT_ID}/00000000-0000-0000-0000-000000000000/g' \
        -e 's/\${WORKER_IDENTITY_NAME}/disabled-worker/g' \
        -e 's/\${WORKER_CLIENT_ID}/00000000-0000-0000-0000-000000000000/g' \
        test-support/local/sqlcmd-session-settings.sql \
        "$migration_file" \
        | docker exec -i assistantcore-sqlserver \
            //opt/mssql-tools18/bin/sqlcmd \
            -b -C -S localhost -U sa -P "$SQL_SERVER_PASSWORD"
done

echo "[2/7] Préparation de l'organisation et de l'administrateur locaux..."
docker exec -i assistantcore-sqlserver \
    //opt/mssql-tools18/bin/sqlcmd \
    -C -S localhost -U sa -P "$SQL_SERVER_PASSWORD" \
    < test-support/local/seed-local.sql

echo "[3/7] Démarrage de WireMock..."
docker compose -f docker-compose.wiremock.yml up -d --force-recreate wiremock

WIREMOCK_READY=false
for _ in {1..60}; do
    if curl --silent --fail --insecure "$WIREMOCK_URL/__admin/mappings" >/dev/null; then
        WIREMOCK_READY=true
        break
    fi
    sleep 1
done

if [[ "$WIREMOCK_READY" != true ]]; then
    echo "WireMock n'a pas répondu après 60 secondes." >&2
    exit 1
fi

echo "[4/7] Compilation de la solution..."
dotnet build Solution.sln

echo "[5/7] Génération du JWT local..."
TOKEN_FILE="$(bash scripts/create-local-jwt.sh)"
LOCAL_ACCESS_TOKEN="$(sed -n '1p' "$TOKEN_FILE")"

jq -n \
    --arg access_token "$LOCAL_ACCESS_TOKEN" \
    '{
        request: {
            method: "GET",
            urlPath: "/local-auth/token"
        },
        response: {
            status: 200,
            headers: {
                "Content-Type": "application/json"
            },
            jsonBody: {
                access_token: $access_token,
                token_type: "Bearer",
                expires_in: 28800
            }
        }
    }' \
    | curl --silent --show-error --fail --insecure \
        --request POST \
        --header "Content-Type: application/json" \
        --data-binary @- \
        "$WIREMOCK_URL/__admin/mappings" >/dev/null

echo "[6/7] Démarrage de l'API locale..."
ASPNETCORE_ENVIRONMENT=Local \
    ASPNETCORE_URLS="https://localhost:7292;http://localhost:5043" \
    dotnet run --no-build --no-launch-profile --project "$SERVICE_PROJECT" &
API_PID=$!

API_READY=false
for _ in {1..60}; do
    if curl --silent --fail --insecure "$API_URL/swagger/index.html" >/dev/null; then
        API_READY=true
        break
    fi
    if ! kill -0 "$API_PID" 2>/dev/null; then
        wait "$API_PID"
    fi
    sleep 1
done

if [[ "$API_READY" != true ]]; then
    echo "L'API n'a pas répondu après 60 secondes." >&2
    exit 1
fi

echo "[7/7] Démarrage du Worker local..."
DOTNET_ENVIRONMENT=Local \
    DOTNET_CONTENTROOT="$WORKER_PROJECT" \
    dotnet run --no-build --project "$WORKER_PROJECT" &
WORKER_PID=$!

echo
echo "Environnement local simulé prêt. Laisse ce terminal ouvert."
echo "API:       $API_URL"
echo "Swagger:   $API_URL/swagger"
echo "WireMock:  $WIREMOCK_URL/__admin"
echo "SQLPad:    http://localhost:3000"
echo "JWT local: $TOKEN_FILE"
echo
echo "Bearer token à utiliser dans Postman:"
sed -n '1p' "$TOKEN_FILE"
echo
echo "Utilise Ctrl+C pour arrêter l'API et le Worker."

while kill -0 "$API_PID" 2>/dev/null && kill -0 "$WORKER_PID" 2>/dev/null; do
    sleep 1
done

if ! kill -0 "$API_PID" 2>/dev/null; then
    wait "$API_PID"
else
    wait "$WORKER_PID"
fi
