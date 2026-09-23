# Préparer les environnements de production

## Table des matières

- [But](#production-purpose)
- [Composants](#production-components)
- [Configuration et secrets](#production-configuration)
- [Santé](#production-health)
- [Exemples de santé](#production-health-examples)
- [Déploiement](#production-deployment)
  - [Structure versionnée](#production-deployment-structure)
  - [Environnements](#production-deployment-environments)
  - [Images immuables](#production-deployment-images)
  - [Publication des images](#production-deployment-publish)
  - [Déploiement CERTIF réel et manuel](#production-deployment-certification)
  - [Secrets](#production-deployment-secrets)
  - [Échecs et retour à la version précédente](#production-deployment-rollback)
- [Observabilité](#production-observability)
- [Sauvegarde et reprise](#production-backup)
- [Sécurité web](#production-web-security)
- [Critères d'acceptation](#production-acceptance)

<a id="production-purpose"></a>
## But

Rendre Angular, l'API, les workers, les webhooks et leurs données déployables,
observables et récupérables sans dépendre d'une procédure implicite.

<a id="production-components"></a>
## Composants

CERTIF définit les hôtes, la région, les dépendances et les responsabilités
pour Angular, l'API, le worker d'ingestion, les webhooks, Azure SQL, Azure AI
Search et le stockage distribué. Ses données restent séparées des tests locaux.

L'hébergement Azure conserve uniquement CERTIF. Le développement avec WireMock
reste local; les changements sont publiés dans ACR après CI, puis déployés à
CERTIF par déclenchement manuel.

<a id="production-configuration"></a>
## Configuration et secrets

Les valeurs non sensibles sont versionnées par environnement. Les secrets sont
chargés depuis Key Vault avec identité managée. Le démarrage échoue clairement
si une valeur obligatoire manque. Aucun secret n'est présent dans le bundle Angular.

<a id="production-health"></a>
## Santé

```http
GET /health/live
GET /health/ready
```

`live` vérifie le processus sans appeler Internet. `ready` vérifie uniquement
les dépendances indispensables pour recevoir du trafic, avec délais courts.
Les détails sensibles ne sont pas publics.

<a id="production-health-examples"></a>
## Exemples de santé

Réponse publique lorsque l’API peut recevoir du trafic :

```json
{
  "status": "Healthy",
  "checks": {
    "sql": "Healthy",
    "distributedStore": "Healthy"
  }
}
```

Une panne SQL retourne `503 Service Unavailable`. La réponse publique ne
contient ni chaîne de connexion, ni nom de serveur, ni exception. Azure AI
Search et les fournisseurs IA sont observés séparément et ne rendent pas l’API
non prête si le chat peut retourner une erreur dégradée contrôlée.

Valeurs initiales : délai maximal de deux secondes par check et cinq secondes
pour la sonde complète. Ces valeurs sont configurables.

<a id="production-deployment"></a>
## Déploiement

Le CI exécute les tests et les workflows publient des images immuables. Un
workflow manuel crée un manifeste de candidat, exécute Flyway, déploie CERTIF
et vérifie sa santé.

Une migration destructive incompatible avec l'ancienne version interdit un
retour sûr à la version précédente. Elle doit être découpée en plusieurs
migrations additives compatibles avec les deux versions de l'application.

<a id="production-deployment-structure"></a>
### Structure versionnée

Le dépôt utilise une définition commune pour les ressources Azure et un fichier
de paramètres par environnement :

```text
deploy/
├── docker/
│   ├── api.Dockerfile
│   ├── worker.Dockerfile
├── infra/
│   ├── bootstrap-shared.bicep
│   ├── bootstrap-environment.bicep
│   ├── main.bicep
│   └── modules/
└── environments/
    ├── shared.bicepparam
    ├── certif.bootstrap.bicepparam
    ├── certif.bicepparam
    ├── prod.bootstrap.bicepparam
    └── prod.bicepparam

.github/workflows/
├── provision-azure.yml
├── publish-images.yml
├── promote-certif.yml
├── control-certif.yml
└── release-candidate.yml
```

`main.bicep` déploie CERTIF ou PROD à partir de la même topologie. Chaque
fichier `.bicepparam` contient uniquement les paramètres non sensibles de son
environnement; les valeurs confidentielles restent dans le Key Vault associé.

<a id="production-deployment-environments"></a>
### Environnements

CERTIF possède :

- une base Azure SQL;
- sa configuration et son Key Vault;
- une identité managée;
- ses données;
- l'API et le worker.

PROD possède son propre groupe de ressources, sa base SQL, son Key Vault, son
stockage de clés Data Protection, ses applications Entra, ses ressources Azure
OpenAI/Foundry/Vision et, après la première promotion, ses Container Apps et son
Front Door. Le service Azure AI Search Basic est partagé pour optimiser le
coût, mais chaque client possède un index, une source et une base de
connaissances distincts. Le premier ensemble PROD est
`ogcomptabilite-index`, `ogcomptabilite-knowledge-source` et
`ogcomptabilite-knowledge-base`.

La base Azure SQL et ses droits sont dédiés à CERTIF. Elle utilise la limite
gratuite serverless et se met en pause après une heure d'inactivité.

Le développement local conserve SQL Server dans Docker. Azure SQL CERTIF sert
à valider Flyway, les contraintes, les transactions, les repositories et la
persistance après un redémarrage.

<a id="production-deployment-images"></a>
### Images immuables

Un ACR Basic partagé stocke les images de candidats. Son compte administrateur
est désactivé. Les Container Apps tirent les images avec une identité managée
ayant seulement le rôle `AcrPull`.

L'API, le BFF, le worker, Flyway et la SPA sont publiés avec un tag basé sur
le SHA complet du commit :

```text
acrassistant<suffix>.azurecr.io/assistant-api:sha-<40 caractères>
acrassistant<suffix>.azurecr.io/assistant-bff:sha-<40 caractères>
acrassistant<suffix>.azurecr.io/assistant-worker:sha-<40 caractères>
acrassistant<suffix>.azurecr.io/assistant-migrations:sha-<40 caractères>
acrassistant<suffix>.azurecr.io/assistant-spa:sha-<40 caractères>
```

Une image n'est jamais reconstruite lors de sa promotion. Le tag `latest` ne
sert jamais de référence de déploiement.

<a id="production-deployment-publish"></a>
### Publication des images

Le développement local conserve WireMock et l'authentification JWT locale.
Après la réussite du CI sur la branche par défaut, les workflows construisent
les images API, BFF, worker, migrations et SPA avec des tags SHA complets et les
publient dans ACR. Ils ne déploient pas d'environnement Azure.

<a id="production-deployment-certification"></a>
### Déploiement CERTIF réel et manuel

CERTIF utilise les véritables services Microsoft Entra, Microsoft Graph,
OpenAI et Azure AI Search. Il utilise la base `AssistantCoreDb` de son serveur
Azure SQL CERTIF et uniquement des données de certification autorisées.

Le déploiement CERTIF est déclenché manuellement avec `workflow_dispatch`.
L'utilisateur fournit le tag backend commun aux images API/BFF/worker/migrations
et le tag SPA publiés après le CI.

Le workflow CERTIF ne reconstruit aucune image. Il :

1. vérifie que le tag immuable existe;
2. vérifie que les images existent dans ACR;
3. vérifie que le callback de consentement Microsoft 365 est enregistré dans Entra;
4. synchronise la clé primaire Azure AI Search dans Key Vault avant de créer les révisions;
5. exécute Flyway sur `assistantcore-certif`;
6. déploie les images par digest;
7. vérifie que chaque conteneur de la dernière révision est réellement démarré et prêt;
8. purge Front Door, puis appelle `/health/live`, `/health/api-ready` et `/bff/session`;
9. vérifie que la SPA répond.

Le déclenchement manuel constitue la décision de promotion. Une protection
GitHub Environment peut exiger une approbation supplémentaire si l'équipe le
souhaite.

Le workflow `control-certif.yml` met l'API, la SPA et le worker à une réplique
au démarrage d'une séance. Il arrête automatiquement le worker après
30 minutes. L'action d'arrêt remet les répliques à zéro et désactive les ingress
publics de l'API et de la SPA.

Le workflow `release-candidate.yml` produit un manifeste JSON versionné avec les cinq
digests ACR exacts des images les plus récemment publiées. Les champs de tag peuvent
rester vides pour utiliser automatiquement les derniers tags SHA backend et SPA,
ou être renseignés pour choisir une version précise. Le workflow CERTIF déploie
ce manifeste sans reconstruire les images.

Après la réussite des migrations et des contrôles CERTIF, le workflow publie
une copie inchangée du manifeste sous le nom `certified-rc-*`. Ce manifeste est
le contrat de promotion vers PROD : le pipeline PROD doit refuser un simple
release candidate, télécharger uniquement un artifact `certified-rc-*`, puis
déployer les mêmes digests API, BFF, Worker, migrations et SPA. Aucun tag ne doit
être résolu à nouveau et aucune image ne doit être reconstruite entre CERTIF et
PROD. Le pipeline PROD doit aussi vérifier que l’artifact provient d’une
exécution réussie du workflow `Promote release candidate to CERTIF`; le nom de
l’artifact seul ne constitue pas une preuve de certification.

Avant d’activer ce pipeline, créer un environnement GitHub `prod` et une
identité OIDC dédiée à PROD. Son identifiant fédéré doit
cibler exactement `environment:prod`; ses rôles Azure doivent être limités au
groupe de ressources PROD et à la lecture de l’ACR partagé. Ne pas réutiliser
l’identité de déploiement CERTIF pour PROD. Les redirect URIs, secrets, clés de
chiffrement, base SQL et identités managées PROD doivent également rester
séparés de CERTIF.

Pour un dépôt privé dont le forfait GitHub ne permet pas les approbateurs
d’environnement, limiter `prod` à la branche par défaut exacte et ne stocker
aucun secret dans GitHub. Ajouter une protection de branche avec revue dès que
le forfait le permet. Les secrets PROD restent dans Key Vault. Le pipeline PROD
accepte uniquement un artifact `certified-rc-*`, valide sa provenance et
déploie ses cinq digests sans reconstruire d’image.

<a id="production-deployment-secrets"></a>
### Secrets

CERTIF conserve les véritables secrets Microsoft 365, le certificat PFX
SharePoint App-Only encodé en Base64 et son mot de passe, ainsi que les secrets
OpenAI, Azure AI Search et Azure SQL.

Les Container Apps accèdent uniquement au coffre de leur environnement avec
une identité managée et le rôle minimal nécessaire pour lire les secrets. Aucun
mot de passe, token, Client Secret, API key ou chaîne de connexion n'est stocké
dans Git, les fichiers Bicep, les workflows ou les images.

GitHub Actions se connecte à Azure avec OIDC et des jetons temporaires. Aucun
Client Secret Azure permanent n'est stocké dans GitHub.

L'identité OIDC de déploiement doit pouvoir écrire uniquement le secret
`azure-search-api-key` de son Key Vault. Cette permission permet au pipeline de
remplacer la clé lorsque la clé primaire Azure AI Search change. Sans elle,
l'API peut démarrer mais la recherche Microsoft 365 échoue avec une réponse
`403` d'Azure AI Search.

<a id="production-deployment-rollback"></a>
### Échecs et retour à la version précédente

La nouvelle révision ne doit pas être considérée comme valide avant la réussite
des migrations, des sondes et des smoke tests. En cas d'échec après le
déploiement, le workflow réactive ou redéploie le dernier SHA fonctionnel.

Le retour applicatif utilise une image déjà publiée. Il ne reconstruit pas
l'ancien commit. Flyway n'exécute jamais automatiquement une migration
destructive inverse.

<a id="production-observability"></a>
## Observabilité

Logs structurés, métriques et traces partagent un correlation ID. Mesurer au
minimum erreurs HTTP, latence, disponibilité, files de worker, appels externes,
rate limits et échecs de purge. Aucun contenu utilisateur ou token n'est journalisé.

Tableau de bord minimal :

| Signal | Alerte initiale | Action attendue |
| --- | --- | --- |
| réponses HTTP 5xx | plus de 5 % pendant 5 minutes | vérifier traces et dépendances |
| file worker | plus de 1 000 messages pendant 10 minutes | vérifier workers et throttling |
| oldest message age | plus de 15 minutes | augmenter capacité ou corriger le blocage |
| échecs de purge permanents | au moins 1 | intervention sécurité/opérations |
| readiness | 3 échecs consécutifs | retirer l’instance du trafic |

Les seuils sont configurables par environnement. Les labels de métriques ne
contiennent ni utilisateur, ni courriel, ni texte de conversation.

<a id="production-backup"></a>
## Sauvegarde et reprise

Définir RPO/RTO, sauvegardes SQL chiffrées, rétention, restauration testée et
procédure de reconstruction des index. Une sauvegarde non restaurée en test
n'est pas considérée comme validée.

Valeurs initiales à confirmer avant production : RPO SQL de 15 minutes et RTO
de 4 heures. Un exercice restaure une sauvegarde dans un environnement isolé,
applique les migrations, vérifie les nombres d’organisations, membres et
conversations, puis exécute les smoke tests. Azure AI Search est reconstruit à
partir des sources et checkpoints; il n’est pas considéré comme la copie
unique des données.

<a id="production-web-security"></a>
## Sécurité web

Forcer HTTPS, HSTS en production, CSP adaptée à Angular/MSAL, en-têtes contre
le sniffing et framing, CORS restrictif et dépendances scannées. Les redirect
URIs Entra correspondent exactement aux domaines déployés.

<a id="production-acceptance"></a>
## Critères d'acceptation

- CERTIF peut être recréé depuis une définition versionnée.
- Le développement avec services simulés reste local.
- CERTIF utilise ses véritables intégrations et une base Azure SQL séparée.
- Un merge publie les images sans déployer Azure.
- CERTIF est déployé manuellement depuis des images immuables publiées dans ACR.
- Aucun fichier ou workflow PROD n'est créé pendant cette étape.
- Secrets, migrations, health checks et retour à la version précédente sont automatisés.
- Alertes et tableaux de bord couvrent les pannes importantes.
- Une restauration est exécutée et mesurée.
- Les contrôles de sécurité web sont vérifiés après déploiement.
