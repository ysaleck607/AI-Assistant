#!/usr/bin/env bash
set -euo pipefail

ENVIRONMENT_NAME="${1:?environment name is required}"

case "$ENVIRONMENT_NAME" in
  certif)
    client_id='558d6670-3549-423e-ae92-c5ff1d3b326b'
    ;;
  prod)
    client_id='f2907152-55be-49ef-b230-9b6821478433'
    ;;
  *)
    echo "environment name must be certif or prod." >&2
    exit 1
    ;;
esac

expected_uri="https://assistant-${ENVIRONMENT_NAME}.onpremia.ca/api/microsoft365/consent/callback"
redirect_uris="$(az ad app show --id "$client_id" --query 'web.redirectUris' --output tsv)"

if ! grep -Fxq "$expected_uri" <<<"$redirect_uris"; then
  echo "Microsoft 365 connector redirect URI is missing: ${expected_uri}" >&2
  exit 1
fi

echo "Microsoft 365 connector redirect URI is configured for ${ENVIRONMENT_NAME}."
