#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:?resource group is required}"
ENVIRONMENT_NAME="${2:?environment name is required}"

profiles="$(az afd profile list \
  --resource-group "$RESOURCE_GROUP" \
  --query "[?starts_with(name, 'afd-assistant-${ENVIRONMENT_NAME}-')].name" \
  --output tsv)"

if [[ "$(wc -w <<<"$profiles")" -ne 1 ]]; then
  echo "Expected exactly one Azure Front Door profile for ${ENVIRONMENT_NAME}." >&2
  exit 1
fi

profile="$profiles"
endpoints="$(az afd endpoint list \
  --resource-group "$RESOURCE_GROUP" \
  --profile-name "$profile" \
  --query '[].name' \
  --output tsv)"

if [[ "$(wc -w <<<"$endpoints")" -ne 1 ]]; then
  echo "Expected exactly one Azure Front Door endpoint for profile ${profile}." >&2
  exit 1
fi

az afd endpoint purge \
  --resource-group "$RESOURCE_GROUP" \
  --profile-name "$profile" \
  --endpoint-name "$endpoints" \
  --content-paths '/*' \
  --no-wait

echo "Azure Front Door cache purge requested for ${profile}."
