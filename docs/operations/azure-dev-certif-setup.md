# Installer et déployer CERTIF dans Azure Container Apps

## Table des matières

- [But](#azure-setup-purpose)
- [Architecture](#azure-setup-architecture)
- [Préparer Azure et GitHub](#azure-setup-prerequisites)
- [Créer les ressources partagées](#azure-setup-shared)
- [Créer CERTIF](#azure-setup-certif)
- [Déployer une version CERTIF](#azure-setup-deployments)
- [Démarrer et arrêter CERTIF](#azure-setup-control-certif)
- [Problèmes fréquents](#azure-setup-troubleshooting)

<a id="azure-setup-purpose"></a>
## But

Ce guide configure le seul environnement Azure de l’application : CERTIF.
DEV Azure n’est plus déployé. Le développement avec WireMock demeure disponible
localement. Le CI publie les images; une action manuelle choisit celles qui
seront déployées dans CERTIF.

<a id="azure-setup-architecture"></a>
## Architecture

```text
Groupe partagé rg-assistant-shared
└── ACR Basic — images API, worker, migrations et SPA

Groupe rg-assistant-certif
├── Azure SQL AssistantCoreDb
├── Key Vault CERTIF
├── Container Apps Environment
│   ├── API
│   ├── SPA
│   ├── Worker
└── Job Flyway
```

Les images portent un tag `sha-<40 caractères>` et sont promues par digest.
Les secrets sont lus depuis Key Vault par l’identité managée de CERTIF.

<a id="azure-setup-prerequisites"></a>
## Préparer Azure et GitHub

Dans Microsoft Entra, créer ou réutiliser l’application `github-assistant-deploy`
et configurer ses identifiants fédérés OIDC pour l’environnement GitHub `certif`
des dépôts BFF et SPA. Les workflows n’utilisent pas de Client Secret Azure.

Attribuer à cette identité les droits nécessaires sur `rg-assistant-shared` et
`rg-assistant-certif` : `Contributor`, `User Access Administrator` pour les
assignations de rôles Bicep, `AcrPush` sur l’ACR et `Key Vault Secrets User`
sur le coffre CERTIF. Après le déploiement, les Container Apps utilisent leurs
propres identités limitées à `AcrPull` et à la lecture Key Vault.

Dans les paramètres Actions des deux dépôts, créer l’environnement `certif`.
Il peut exiger un approbateur. Ajouter ces variables dans cet environnement :

| Variable | Exemple ou contenu |
| --- | --- |
| `AZURE_CLIENT_ID` | Application ID de `github-assistant-deploy` |
| `AZURE_TENANT_ID` | ID du tenant Azure |
| `AZURE_SUBSCRIPTION_ID` | ID de la souscription |
| `AZURE_NAME_SUFFIX` | suffixe global de 3 à 8 caractères |
| `AZURE_ACR_NAME` | par exemple `acrassistantonp01` |
| `AZURE_SHARED_RESOURCE_GROUP` | `rg-assistant-shared` |
| `AZURE_CERTIF_RESOURCE_GROUP` | `rg-assistant-certif` |

Dans le dépôt BFF, l’environnement GitHub `publish-images` est dédié à la
publication automatique des images après un CI réussi sur `master`. Il n'exige
pas d'approbation. Les variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`,
`AZURE_SUBSCRIPTION_ID` et `AZURE_ACR_NAME` sont définies au niveau du dépôt et
sont donc accessibles à cet environnement. L’environnement `certif` conserve
son approbation pour les opérations de déploiement.

<a id="azure-setup-shared"></a>
## Créer les ressources partagées

Dans le dépôt BFF, lancer **Provision Azure infrastructure** avec
`bootstrap-shared` et `certif`. Le workflow crée `rg-assistant-shared` et
l’ACR Basic si nécessaire. Vérifier que `github-assistant-deploy` possède
`AcrPush` sur le registre.

<a id="azure-setup-certif"></a>
## Créer CERTIF

Lancer **Provision Azure infrastructure** avec `bootstrap-environment` et
`certif`. Le workflow crée le groupe et le coffre `kv-assistant-cert-<suffix>`.
Attribuer `Key Vault Secrets User` à l’identité GitHub et
`Key Vault Secrets Officer` à la personne qui gère les valeurs.

Déposer ces secrets dans le coffre avant de déployer :

| Nom | Contenu |
| --- | --- |
| `sql-admin-password` | mot de passe robuste pour Azure SQL CERTIF |
| `microsoft365-client-secret` | secret de l’application Microsoft 365 CERTIF |
| `microsoft365-clientstate-hmac-key` | clé aléatoire d’au moins 32 caractères |
| `microsoft365-sharepoint-certificate-pfx` | PFX SharePoint App-Only encodé en Base64 |
| `microsoft365-sharepoint-certificate-password` | mot de passe du PFX |
| `azure-vision-ocr-api-key` | clé de la ressource Azure AI Vision |
| `azure-openai-embedding-api-key` | clé Azure OpenAI pour les embeddings |
| `azure-openai-planning-api-key` | clé Azure OpenAI pour la planification de recherche |
| `azure-search-api-key` | clé Azure AI Search CERTIF |

Ne copier aucune de ces valeurs dans Git, les paramètres Bicep, les variables
GitHub ou une image Docker.

Après un CI réussi sur la branche par défaut, les workflows **Publish backend
images** et **Publish SPA image** construisent et publient les images avec un
tag SHA complet. Ils ne déploient pas d’application Azure.

Pour créer ou mettre à jour les applications, lancer **Provision Azure
infrastructure** avec `deploy-environment` et `certif`. Les tags backend et SPA
peuvent rester vides : le workflow choisit le dernier tag SHA disponible dans
ACR (un tag backend seulement si l’API, le Worker et les migrations sont tous
présents). Il est aussi possible de saisir des tags précis. Le workflow exécute
un `what-if`, puis met à jour les ressources CERTIF. Le job Flyway est créé,
mais les migrations sont
exécutées lors de la promotion d’une version.

L’API et le Worker CERTIF utilisent le modèle de planification RAG `gpt-5.5`
avec l’effort de récupération `auto`. Dans Azure, le nom de déploiement de ce
modèle est `gpt-5.5-1`. Les embeddings, le découpage des documents, le
classement sémantique et les limites de contexte reprennent les mêmes réglages
que la configuration locale.

<a id="azure-setup-deployments"></a>
## Déployer une version CERTIF

1. Dans le workflow **Create release candidate** du dépôt BFF, laisser les tags
   backend et SPA vides pour prendre les dernières images publiées, ou indiquer
   les tags SHA `sha-...` voulus.
2. Vérifier que le manifeste produit référence les quatre images ACR par digest.
3. Lancer **Promote release candidate to CERTIF** dans le dépôt BFF et choisir
   l’artifact candidat.
4. Vérifier Flyway, les endpoints `/health/live` et `/health/ready`, puis le
   chargement de la SPA dans le résumé du workflow.

Pour promouvoir seulement la SPA, le dépôt SPA offre aussi **Promote SPA to
CERTIF**. Le tag SHA est facultatif : sans tag, le workflow choisit le plus
récent dans ACR. Cette promotion conserve l’approbation de l’environnement
GitHub `certif`, puis vérifie la SPA et sa configuration runtime.

<a id="azure-setup-control-certif"></a>
## Démarrer et arrêter CERTIF

Dans le dépôt BFF, lancer **Start or stop CERTIF** avec `start` et `all` avant
une séance. Cette action remet l’API et la SPA à une réplique et réactive leurs
ingress publics; le worker démarre aussi, puis s’arrête automatiquement après
30 minutes. Pour réduire le calcul du worker seulement, choisir le scope
`worker`. L’action `stop` avec `all` arrête le worker et les applications
publiques, puis désactive leurs ingress.

<a id="azure-setup-troubleshooting"></a>
## Problèmes fréquents

### Échec OIDC

Vérifier que l’identifiant fédéré correspond au bon dépôt et à l’environnement
GitHub `certif`, et que les variables Azure sont présentes dans cet
environnement.

### Secret Key Vault introuvable

Vérifier le nom exact, le coffre `kv-assistant-cert-<suffix>` et le rôle
`Key Vault Secrets User` de l’identité managée `id-assistant-workload-certif`.

### Échec Bicep pendant `getSecret`

Vérifier que `sql-admin-password` existe, que le déploiement de modèles ARM est
activé sur Key Vault et que l’identité GitHub peut lire les secrets.

### CERTIF ne répond pas après un arrêt

Lancer **Start or stop CERTIF** avec `start` et `all`. Remettre les répliques à
zéro ne réactive pas un ingress désactivé.
