#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:?resource group is required}"
APP_NAME="${2:?container app name is required}"
EXPECTED_IMAGES_JSON="${3:-}"

if [[ -z "$EXPECTED_IMAGES_JSON" ]]; then
  EXPECTED_IMAGES_JSON='{}'
fi

if ! jq -e 'type == "object" and all(to_entries[]; (.key | type == "string") and (.value | type == "string"))' \
  <<<"$EXPECTED_IMAGES_JSON" >/dev/null; then
  echo "Expected container images must be a valid JSON object." >&2
  exit 2
fi

revision="$(az containerapp show \
  --resource-group "$RESOURCE_GROUP" \
  --name "$APP_NAME" \
  --query properties.latestRevisionName \
  --output tsv)"

if [[ -z "$revision" ]]; then
  echo "Container App ${APP_NAME} has no latest revision." >&2
  exit 1
fi

for attempt in $(seq 1 24); do
  replicas="$(az containerapp replica list \
    --resource-group "$RESOURCE_GROUP" \
    --name "$APP_NAME" \
    --revision "$revision" \
    --output json)"

  if jq -e '
    length > 0
    and all(.[].properties.containers[]; .ready == true and .runningState == "Running")
  ' <<<"$replicas" >/dev/null; then
    actual_images="$(az containerapp revision show \
      --resource-group "$RESOURCE_GROUP" \
      --name "$APP_NAME" \
      --revision "$revision" \
      --query 'properties.template.containers[].{name:name,image:image}' \
      --output json)"

    images_match=true
    while IFS=$'\t' read -r container_name expected_image; do
      actual_image="$(jq -r --arg containerName "$container_name" '
        first(.[] | select(.name == $containerName) | .image) // ""
      ' <<<"$actual_images")"

      if [[ "$actual_image" != "$expected_image" ]]; then
        images_match=false
        break
      fi
    done < <(jq -r 'to_entries[] | [.key, .value] | @tsv' <<<"$EXPECTED_IMAGES_JSON")

    if [[ "$images_match" == true ]]; then
      echo "Container App ${APP_NAME} revision ${revision} is ready."
      exit 0
    fi

    echo "Container App ${APP_NAME} revision ${revision} does not use the expected images." >&2
    echo "Expected: ${EXPECTED_IMAGES_JSON}" >&2
    echo "Actual:" >&2
    echo "$actual_images" >&2
    exit 1
  fi

  if [[ "$attempt" -eq 24 ]]; then
    echo "Container App ${APP_NAME} revision ${revision} did not become ready." >&2
    echo "$replicas" >&2
    exit 1
  fi

  sleep 5
done
