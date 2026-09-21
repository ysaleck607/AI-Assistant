#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:?environment resource group is required}"
ENVIRONMENT_NAME="${2:?environment name is required}"
NAME_SUFFIX="${3:?name suffix is required}"
SHARED_RESOURCE_GROUP="${4:?shared resource group is required}"

if [[ "$ENVIRONMENT_NAME" != "certif" && "$ENVIRONMENT_NAME" != "prod" ]]; then
  echo "environment name must be certif or prod." >&2
  exit 1
fi

key_vault_environment="$ENVIRONMENT_NAME"
if [[ "$ENVIRONMENT_NAME" == "certif" ]]; then
  key_vault_environment="cert"
fi

SEARCH_SERVICE="srch-assistant-${NAME_SUFFIX}"
KEY_VAULT="kv-assistant-${key_vault_environment}-${NAME_SUFFIX}"

search_key="$(az search admin-key show \
  --resource-group "$SHARED_RESOURCE_GROUP" \
  --service-name "$SEARCH_SERVICE" \
  --query primaryKey \
  --output tsv)"

if [[ -z "$search_key" ]]; then
  echo "Azure AI Search did not return a primary admin key." >&2
  exit 1
fi

az keyvault secret set \
  --vault-name "$KEY_VAULT" \
  --name azure-search-api-key \
  --value "$search_key" \
  --output none

key_vault_secret_uri="https://${KEY_VAULT}.vault.azure.net/secrets/azure-search-api-key"

for workload in api worker; do
  app_name="ca-assistant-${workload}-${ENVIRONMENT_NAME}-p"
  identity_name="id-assistant-${workload}-${ENVIRONMENT_NAME}"

  if ! az containerapp show --resource-group "$RESOURCE_GROUP" --name "$app_name" --output none 2>/dev/null; then
    continue
  fi

  identity_id="$(az identity show \
    --resource-group "$RESOURCE_GROUP" \
    --name "$identity_name" \
    --query id \
    --output tsv)"

  az containerapp secret set \
    --resource-group "$RESOURCE_GROUP" \
    --name "$app_name" \
    --secrets "azure-search-api-key=keyvaultref:${key_vault_secret_uri},identityref:${identity_id}" \
    --output none
done

echo "Azure AI Search key synchronized to Key Vault ${KEY_VAULT}."
