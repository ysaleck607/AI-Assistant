#!/usr/bin/env bash
set -euo pipefail

RESOURCE_GROUP="${1:?resource group is required}"
APP_NAME="${2:?container app name is required}"
EXPECTED_IMAGES_JSON="${3:-{}}"

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

    if jq -e --argjson expected "$EXPECTED_IMAGES_JSON" --argjson actual "$actual_images" '
      all($expected | to_entries[] as $expectedImage;
        any($actual[]; .name == $expectedImage.key and .image == $expectedImage.value))
    ' >/dev/null; then
      echo "Container App ${APP_NAME} revision ${revision} is ready."
      exit 0
    fi

    echo "Container App ${APP_NAME} revision ${revision} does not use the expected images." >&2
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
