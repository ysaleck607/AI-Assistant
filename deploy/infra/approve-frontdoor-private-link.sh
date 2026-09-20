#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:?resource group is required}"
ENVIRONMENT_NAME="${2:?environment name is required}"

CONTAINER_ENV="cae-assistant-${ENVIRONMENT_NAME}-private"
REQUEST_MESSAGE="OnPremia Front Door private origin"

ENV_ID="$(az containerapp env show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$CONTAINER_ENV" \
  --query id -o tsv)"

if [ -z "$ENV_ID" ]; then
  echo "Container Apps environment ${CONTAINER_ENV} was not found." >&2
  exit 1
fi

mapfile -t MATCHING_CONNECTIONS < <(az network private-endpoint-connection list \
  --id "$ENV_ID" \
  --query "[?properties.privateLinkServiceConnectionState.description=='${REQUEST_MESSAGE}'].id" \
  -o tsv 2>/dev/null || true)

if [ "${#MATCHING_CONNECTIONS[@]}" -eq 0 ]; then
  echo "No Azure Front Door private endpoint request was found on ${CONTAINER_ENV}." >&2
  exit 2
fi

for connection_id in "${MATCHING_CONNECTIONS[@]}"; do
  [ -z "$connection_id" ] && continue

  status="$(az network private-endpoint-connection show \
    --id "$connection_id" \
    --query properties.privateLinkServiceConnectionState.status \
    -o tsv 2>/dev/null || true)"

  case "$status" in
    Approved)
      ;;
    Pending)
      az network private-endpoint-connection approve \
        --id "$connection_id" \
        --description "Approved by OnPremia infrastructure workflow" \
        --only-show-errors >/dev/null
      ;;
    *)
      echo "Unexpected Front Door private endpoint state '${status}' for ${connection_id}." >&2
      exit 3
      ;;
  esac
done

for connection_id in "${MATCHING_CONNECTIONS[@]}"; do
  [ -z "$connection_id" ] && continue
  status="$(az network private-endpoint-connection show \
    --id "$connection_id" \
    --query properties.privateLinkServiceConnectionState.status \
    -o tsv)"
  if [ "$status" != "Approved" ]; then
    echo "Front Door private endpoint is not approved: ${connection_id} (${status})." >&2
    exit 4
  fi
done

echo "Azure Front Door private origin approved for ${CONTAINER_ENV}."
