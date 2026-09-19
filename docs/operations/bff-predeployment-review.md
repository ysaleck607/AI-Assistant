# BFF pre-deployment integration review

This review covers the coordinated backend branch `feature/bff-production-hardening`
and SPA branch `feature/bff-authentication` before any CERTIF deployment.

## Implemented architecture

- Browser traffic uses one public origin for Angular, `/bff/*`, OIDC callbacks and `/api/*`.
- The browser keeps only an HttpOnly/Secure BFF session cookie; Entra access tokens stay server-side.
- Unsafe `/api/*` methods require ASP.NET Core antiforgery validation.
- Protected BFF/API paths return HTTP 401/403 instead of redirecting API calls to OpenID Connect.
- BFF logout destroys the local session with a CSRF-protected POST and then starts Entra federated sign-out.
- The BFF proxy strips browser `Authorization`, `Cookie`, and downstream `Set-Cookie` headers.
- SSE uses `ResponseHeadersRead`, disabled buffering and explicit flushes through BFF and Nginx.
- BFF Data Protection keys are persisted in Blob Storage and protected by Key Vault.
- API/Worker/BFF/migrations use distinct user-assigned managed identities; ACR pull is the only shared identity.
- Key Vault secret access is scoped to the secrets consumed by each workload.
- API and Worker connect to Azure SQL with managed identity and receive only `db_datareader` + `db_datawriter`; Flyway remains the DDL/admin path.
- Azure SQL is created with public network access disabled and is reached through a Private Endpoint and private DNS.
- The Container Apps Environment is VNet-integrated, internal, and has public network access disabled.
- AssistantCore.Service uses internal ingress only.
- The SPA/BFF Container App is exposed only through Azure Front Door Premium Private Link.
- Azure Front Door WAF runs in Prevention mode with the Microsoft default and bot managed rule sets.
- The Front Door default-domain route is disabled; the custom domain is the intended public entry point.
- Microsoft 365 consent callback and Graph webhook remain public paths at the same origin and are reverse-proxied by Nginx to the internal API.
- The Graph webhook has edge and application request limits and deduplicates work per subscription before SQL processing.
- API, BFF, Worker and SPA runtime containers run non-root.
- CI audits NuGet/npm dependencies; image publication scans HIGH/CRITICAL vulnerabilities and emits CycloneDX SBOMs.
- GitHub Actions modified by this hardening work are pinned to immutable commits.

## Deployment model

`deploy/infra/main.bicep` now declares the final secure topology directly. The deployment workflow no longer creates a broad shared workload identity, public API ingress, direct-SPA MSAL configuration, or runtime SQL administrator secret and then mutates those resources afterward.

The previous transition-only resources/scripts have been removed:

- `bff-sidecar-overlay.bicep`
- `separate-workload-identities.sh`
- `configure-sql-managed-identities.sh`
- `harden-api-ingress.sh`
- `cutover-sql-private-network.sh`

The only post-deployment infrastructure action is approval of the Azure Front Door Private Link request. This does not harden an already-public origin: the Container Apps Environment is private from creation and remains unreachable until the private endpoint is approved.

## Required before first CERTIF deployment

1. Confirm a confidential BFF Entra application exists.
2. Configure Web redirect URI `https://assistant-certif.onpremia.ca/signin-oidc` and signed-out callback as required by the BFF.
3. Grant delegated permission `api://f70fb50b-52d5-4346-b769-1121cb3ab3e2/access_as_user` to the BFF application and complete any required admin consent.
4. Set GitHub environment variable `AZURE_BFF_ENTRA_CLIENT_ID` to that confidential application client id.
5. Seed Key Vault secret `bff-entra-client-secret`.
6. Confirm the Data Protection storage account/container and Key Vault encryption key exist.
7. Ensure DNS for `assistant-certif.onpremia.ca` is configured for Azure Front Door custom-domain validation and points to the Front Door endpoint before traffic cutover.
8. Ensure the `Microsoft.Cdn` provider is registered in the Azure subscription.
9. Build and test both branches through pull-request CI.

## Required CERTIF validation after deployment

Validate at minimum:

- Front Door custom-domain TLS and WAF association.
- Front Door Private Link status is `Approved`.
- Container Apps Environment `publicNetworkAccess` is `Disabled`.
- AssistantCore.Service ingress is internal.
- Azure SQL `publicNetworkAccess` is `Disabled` and resolves privately from Container Apps.
- Flyway completes successfully and provisions API/Worker database users.
- `/health/live` and `/health/ready`.
- login, logout and session recovery.
- browser requests contain no Entra bearer token.
- CSRF rejection for unsafe calls without the token.
- Microsoft 365 consent callback.
- Graph webhook validation and notifications.
- chat SSE streaming.
- ingestion catch-up and Microsoft Lists processing.

## Known non-blocking follow-up

The BFF token acquisition cache is currently in-memory. With the CERTIF topology fixed at one SPA/BFF replica this is acceptable for the first validation, but a BFF revision restart can require reauthentication even though Data Protection preserves the session cookie. Before scaling BFF to multiple replicas, introduce a distributed Microsoft.Identity.Web token cache (for example Redis) and then raise `maxReplicas`.

GitHub artifact attestations can be added only when the private repository is hosted on a plan that supports private-repository attestations (GitHub Enterprise Cloud). Image scanning and SBOM generation remain enforced regardless of that optional capability.
