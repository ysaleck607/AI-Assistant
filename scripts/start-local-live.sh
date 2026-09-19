#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SERVICE_PROJECT="$ROOT_DIR/AssistantCore.Service"
BFF_PROJECT="$ROOT_DIR/AssistantCore.Bff"
WORKER_PROJECT="$ROOT_DIR/AssistantCore.Ingestion.Worker"
LOCAL_LIVE_SETTINGS="$SERVICE_PROJECT/appsettings.LocalLive.json"
API_URL="https://localhost:7292"
API_HEALTH_URL="http://localhost:5043/health/live"
BFF_URL="http://localhost:7293"
BFF_HEALTH_URL="http://localhost:7293/health/live"
LOCAL_LIVE_ENVIRONMENT="LocalLive"
API_PID=""
BFF_PID=""
WORKER_PID=""

read_is_secured() {
    local value
    value="$(sed -nE 's/^[[:space:]]*"IS_SECURED"[[:space:]]*:[[:space:]]*(true|false)[[:space:]]*,?[[:space:]]*$/\1/p' "$LOCAL_LIVE_SETTINGS" | head -n 1)"
    if [[ "$value" != "true" && "$value" != "false" ]]; then
        echo "IS_SECURED doit être défini à true ou false dans $LOCAL_LIVE_SETTINGS." >&2
        exit 1
    fi
    printf '%s' "$value"
}

cleanup() {
    if [[ -n "$WORKER_PID" ]] && kill -0 "$WORKER_PID" 2>/dev/null; then
        kill "$WORKER_PID"
    fi
    if [[ -n "$BFF_PID" ]] && kill -0 "$BFF_PID" 2>/dev/null; then
        kill "$BFF_PID"
    fi
    if [[ -n "$API_PID" ]] && kill -0 "$API_PID" 2>/dev/null; then
        kill "$API_PID"
    fi
}

trap cleanup EXIT INT TERM

for command_name in docker dotnet curl; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        echo "Commande requise introuvable: $command_name" >&2
        exit 1
    fi
done

if [[ ! -f "$ROOT_DIR/.env.database" ]]; then
    echo "Le fichier .env.database est requis. Consulte la section de test local du README." >&2
    exit 1
fi

if [[ ! -f "$LOCAL_LIVE_SETTINGS" ]]; then
    echo "Configuration LocalLive introuvable: $LOCAL_LIVE_SETTINGS" >&2
    exit 1
fi

IS_SECURED="$(read_is_secured)"

set -a
source "$ROOT_DIR/.env.database"
set +a

if [[ -z "${SQL_SERVER_PASSWORD:-}" ]]; then
    echo "SQL_SERVER_PASSWORD est requis dans .env.database." >&2
    exit 1
fi

DATABASE_CONNECTION_STRING="Server=localhost,1433;Database=AssistantCoreDb;User Id=sa;Password=${SQL_SERVER_PASSWORD};TrustServerCertificate=True;Encrypt=False"

cd "$ROOT_DIR"

echo "[1/6] Démarrage de SQL Server et application des migrations..."
bash scripts/start-database-stack.sh

echo "[2/6] Chargement de la configuration LocalLive versionnée..."
echo "      IS_SECURED=$IS_SECURED"

echo "[3/6] Compilation..."
dotnet build Solution.sln
if [[ "$IS_SECURED" == "true" ]]; then
    dotnet build "$BFF_PROJECT/AssistantCore.Bff.csproj"
fi

echo "[4/6] Démarrage de l'API sur $API_URL..."
if curl --silent --output /dev/null --max-time 1 "$API_HEALTH_URL"; then
    echo "Un service utilise déjà le port local 5043. Arrête-le avant de relancer ce script." >&2
    exit 1
fi

ASPNETCORE_ENVIRONMENT="$LOCAL_LIVE_ENVIRONMENT" \
    ASPNETCORE_URLS="https://localhost:7292;http://localhost:5043" \
    AZURE_TOKEN_CREDENTIALS=AzureCliCredential \
    ConnectionStrings__AssistantCoreDatabase="$DATABASE_CONNECTION_STRING" \
    dotnet run --no-build --no-launch-profile --project "$SERVICE_PROJECT" &
API_PID=$!

API_READY=false
for _ in {1..60}; do
    if ! kill -0 "$API_PID" 2>/dev/null; then
        wait "$API_PID"
    fi
    if curl --silent --fail "$API_HEALTH_URL" >/dev/null; then
        API_READY=true
        break
    fi
    sleep 1
done

if [[ "$API_READY" != true ]]; then
    echo "L'API n'a pas répondu après 60 secondes." >&2
    exit 1
fi

if [[ "$IS_SECURED" == "true" ]]; then
    echo "[5/6] Démarrage du BFF sécurisé sur $BFF_URL..."

    BFF_CLIENT_SECRET="$(dotnet user-secrets list --project "$BFF_PROJECT/AssistantCore.Bff.csproj" 2>/dev/null \
        | sed -n 's/^AzureAd:ClientSecret = //p' \
        | head -n 1)"
    if [[ -z "$BFF_CLIENT_SECRET" ]]; then
        echo "Le mode sécurisé requiert le secret BFF Entra." >&2
        echo "Configure-le avec:" >&2
        echo "  dotnet user-secrets set 'AzureAd:ClientSecret' '<secret>' --project AssistantCore.Bff/AssistantCore.Bff.csproj" >&2
        exit 1
    fi

    ASPNETCORE_ENVIRONMENT="$LOCAL_LIVE_ENVIRONMENT" \
        ASPNETCORE_URLS="$BFF_URL" \
        AZURE_TOKEN_CREDENTIALS=AzureCliCredential \
        dotnet run --no-build --no-launch-profile --project "$BFF_PROJECT" &
    BFF_PID=$!

    BFF_READY=false
    for _ in {1..60}; do
        if ! kill -0 "$BFF_PID" 2>/dev/null; then
            wait "$BFF_PID"
        fi
        if curl --silent --fail "$BFF_HEALTH_URL" >/dev/null; then
            BFF_READY=true
            break
        fi
        sleep 1
    done

    if [[ "$BFF_READY" != true ]]; then
        echo "Le BFF n'a pas répondu après 60 secondes." >&2
        exit 1
    fi
else
    echo "[5/6] BFF désactivé (IS_SECURED=false)."
fi

echo "[6/6] Démarrage du Worker local..."
DOTNET_ENVIRONMENT="$LOCAL_LIVE_ENVIRONMENT" \
    DOTNET_CONTENTROOT="$WORKER_PROJECT" \
    ConnectionStrings__AssistantCoreDatabase="$DATABASE_CONNECTION_STRING" \
    dotnet run --no-build --project "$WORKER_PROJECT" &
WORKER_PID=$!

echo
echo "Environnement connecté aux vrais services prêt. Laisse ce terminal ouvert."
echo "API:     $API_URL"
echo "Health:  $API_HEALTH_URL"
echo "Webhook: consulte Microsoft365:WebhookBaseUrl dans appsettings.LocalLive.json"
echo "Démarre ngrok séparément si nécessaire avec le domaine LocalLive configuré."
echo "SQLPad:  http://localhost:3000"

if [[ "$IS_SECURED" == "true" ]]; then
    echo "BFF:     $BFF_URL"
    echo
    echo "Mode sécurisé actif: démarre la SPA avec 'npm run start:secured'."
    echo "Ouvre ensuite https://localhost:4200."
    echo "Le redirect URI Entra du BFF doit inclure https://localhost:4200/signin-oidc."
else
    echo
    echo "Mode LocalLive classique: démarre la SPA avec 'npm run start:certification'."
    echo "Dans Postman: AuthenticateUser -> Start Consent -> Register Site -> Get Drives -> Enable Drive -> Send Message"
fi

echo "Utilise Ctrl+C pour arrêter l'API, le BFF éventuel et le Worker."

while kill -0 "$API_PID" 2>/dev/null \
    && kill -0 "$WORKER_PID" 2>/dev/null \
    && { [[ -z "$BFF_PID" ]] || kill -0 "$BFF_PID" 2>/dev/null; }; do
    sleep 1
done

if ! kill -0 "$API_PID" 2>/dev/null; then
    wait "$API_PID"
elif [[ -n "$BFF_PID" ]] && ! kill -0 "$BFF_PID" 2>/dev/null; then
    wait "$BFF_PID"
else
    wait "$WORKER_PID"
fi
