# Indexer les documents SharePoint dans Azure AI Search

## Table des matières

- [Métrique vectorielle et migration](#vector-metric-migration)
- [But](#m365-sharepoint-purpose)
- [Résultat attendu](#m365-sharepoint-result)
- [Périmètre de la première version](#m365-sharepoint-scope)
- [Notions importantes](#m365-sharepoint-concepts)
- [Architecture générale](#m365-sharepoint-architecture)
- [Responsabilités des composants](#m365-sharepoint-components)
- [Connexion du tenant Microsoft 365](#m365-sharepoint-onboarding)
- [Permissions Microsoft nécessaires](#m365-sharepoint-permissions)
- [Sites, bibliothèques et listes autorisés](#m365-sharepoint-sources)
- [Types de contenus Microsoft 365](#m365-sharepoint-content-types)
- [Données conservées dans la base](#m365-sharepoint-persistence)
- [Création des webhooks](#m365-sharepoint-webhooks)
- [Renouvellement des webhooks](#m365-sharepoint-webhook-renewal)
- [Synchronisation initiale](#m365-sharepoint-initial-sync)
- [Synchronisation des changements](#m365-sharepoint-delta-sync)
- [Réindexation administrative d’un client](#m365-sharepoint-admin-reindex)
- [Traitement par le worker](#m365-sharepoint-worker)
- [Téléchargement et extraction](#m365-sharepoint-extraction)
- [Traitement des archives](#m365-sharepoint-archives)
- [Traitement des pages et listes](#m365-sharepoint-pages-lists)
- [Traitement de OneDrive](#m365-sharepoint-onedrive)
- [Découpage en passages](#m365-sharepoint-chunking)
- [Création des embeddings](#m365-sharepoint-embeddings)
- [Structure de l’index Azure AI Search](#m365-sharepoint-search-index)
- [Mise à jour et suppression](#m365-sharepoint-index-updates)
- [Respect des permissions](#m365-sharepoint-document-security)
- [Groupes SharePoint locaux](#m365-sharepoint-local-groups)
- [Tester les groupes SharePoint locaux](#m365-sharepoint-local-groups-testing)
- [Connecteur de recherche Microsoft 365](#m365-sharepoint-search-connector)
- [Gestion des erreurs](#m365-sharepoint-errors)
- [Configuration et secrets](#m365-sharepoint-configuration)
- [Développement local](#m365-sharepoint-local-development)
- [Test complet avec le tenant fictif](#m365-sharepoint-end-to-end-test)
- [Stratégie des tickets et des tests](#m365-sharepoint-test-tickets)
- [Ordre d’implémentation](#m365-sharepoint-implementation-order)
- [Définition de terminé](#m365-sharepoint-definition-of-done)
- [Références](#m365-sharepoint-references)

<a id="m365-sharepoint-purpose"></a>
## But

Cette fonctionnalité permet à une organisation de connecter son environnement
Microsoft 365 à AssistantCore.

AssistantCore récupère les documents SharePoint autorisés, extrait leur texte
et les ajoute dans Azure AI Search. Les utilisateurs peuvent ensuite poser une
question à l’assistant et retrouver les documents auxquels ils ont accès.

L’ingestion des documents est indépendante de `POST /api/messages`. Une
question utilisateur ne doit jamais déclencher une synchronisation complète
de SharePoint.

<a id="m365-sharepoint-result"></a>
## Résultat attendu

À la fin de l’implémentation :

1. un administrateur connecte le tenant Microsoft 365 fictif;
2. AssistantCore découvre les sites et bibliothèques SharePoint;
3. un administrateur choisit les sites à indexer;
4. un worker récupère les documents;
5. le texte est découpé en passages;
6. un embedding est créé pour chaque passage;
7. les passages sont ajoutés dans Azure AI Search;
8. un nouveau fichier est détecté et indexé;
9. un fichier modifié remplace son ancienne version;
10. un fichier supprimé disparaît de l’index;
11. une modification de permissions met à jour l’accès au document;
12. le connecteur `search_microsoft_365` retrouve les passages autorisés;
13. la réponse de l’assistant cite le document SharePoint utilisé.

<a id="m365-sharepoint-scope"></a>
## Périmètre de la première version

La première version couvre :

- SharePoint Online;
- les bibliothèques de documents;
- les pages SharePoint modernes;
- les listes SharePoint et leurs éléments;
- le OneDrive professionnel individuel des utilisateurs du tenant;
- les documents Word;
- les classeurs Excel;
- les présentations PowerPoint;
- les PDF contenant du texte;
- les PDF ou images qui demandent de l’OCR;
- les fichiers texte, CSV, JSON, XML, HTML et Markdown;
- les formats OpenDocument;
- les courriels enregistrés comme fichiers EML ou MSG;
- les archives ZIP et GZ;
- les autres formats contenant du texte ou des images extractibles;
- la synchronisation initiale;
- la détection des créations, modifications et suppressions;
- la détection des changements de partage;
- la recherche textuelle et vectorielle;
- les permissions accordées à des utilisateurs Microsoft Entra;
- les permissions accordées à des groupes Microsoft Entra;
- les utilisateurs invités externes autorisés dans le tenant du client;
- les liens « Toute personne disposant du lien »;
- les liens « Personnes de l’organisation disposant du lien »;
- les liens destinés à des personnes précises;
- les tests avec le tenant Microsoft 365 fictif.

La première version ne couvre pas :

- les fichiers audio;
- les fichiers vidéo;
- les exécutables et fichiers binaires sans texte ou image extractible;
- les groupes créés directement dans SharePoint qui ne correspondent pas à un
  groupe Microsoft Entra;
- le OneDrive grand public d’un compte Outlook.com ou Hotmail;
- Outlook et les messages Teams.

Un type de fichier non audio ou vidéo doit être accepté par le pipeline, puis
orienté vers l’extracteur approprié. Si aucun texte ni aucune image utile ne
peut en être extrait, le fichier est enregistré comme traité sans contenu
indexable. Il ne doit pas faire échouer la synchronisation complète.

Un document dont les permissions ne peuvent pas être comprises de manière
fiable ne doit jamais devenir visible dans la recherche.

<a id="m365-sharepoint-concepts"></a>
## Notions importantes

### Site SharePoint

Un site est un espace SharePoint appartenant à une organisation.

Exemple :

```text
https://contoso.sharepoint.com/sites/ressources-humaines
```

### Bibliothèque

Une bibliothèque est un espace de fichiers à l’intérieur d’un site. Microsoft
Graph la représente comme un `drive`.

### Worker

Le worker est une application backend séparée qui exécute les tâches longues :

- lire les changements SharePoint;
- télécharger les documents;
- extraire le texte;
- créer les passages;
- demander les embeddings;
- écrire dans Azure AI Search.

### Webhook

Le webhook est une URL publique appelée par Microsoft lorsqu’une bibliothèque
change. La notification ne contient pas nécessairement le fichier complet.
Elle indique au worker qu’il doit vérifier les changements.

### Passage ou chunk

Un passage est une petite partie du texte d’un document. Un document long
produit plusieurs passages.

### Embedding

Un embedding est une représentation numérique d’un passage. Il permet de
retrouver un texte qui exprime une idée proche même si les mots ne sont pas
exactement les mêmes.

### Delta link

Le `deltaLink` est une URL opaque retournée par Microsoft Graph. Elle permet de
demander uniquement les changements survenus depuis la dernière
synchronisation.

<a id="m365-sharepoint-architecture"></a>
## Architecture générale

```text
Microsoft Graph
      |
      | notification HTTPS
      v
AssistantCore Webhooks
      |
      | message court
      v
Azure Service Bus
      |
      v
AssistantCore Ingestion Worker
      |
      +--> Microsoft Graph : fichiers et permissions
      |
      +--> Extracteur : texte lisible
      |
      +--> Fournisseur d’embeddings
      |
      +--> Azure AI Search : passages indexés

Utilisateur
      |
      v
POST /api/messages
      |
      v
Connecteur search_microsoft_365
      |
      v
Azure AI Search
```

Les appels vers Microsoft Graph, Azure Service Bus, le fournisseur
d’embeddings et Azure AI Search suivent obligatoirement la chaîne :

```text
Service applicatif
  -> interface applicative
  -> adaptateur Infrastructure
  -> client AssistantCore.ExternalServices
  -> SDK ou API externe
```

Le worker ne doit pas appeler directement un SDK externe.

<a id="m365-sharepoint-components"></a>
## Responsabilités des composants

### AssistantCore.Service

Le backend principal :

- expose les actions d’administration du connecteur;
- retourne les sites disponibles;
- permet d’activer ou désactiver un site;
- expose le connecteur de recherche Microsoft 365;
- applique l’organisation et les permissions lors des recherches.

Les controllers injectent uniquement `IDispatcher`.

### AssistantCore Webhooks

Un petit hôte HTTP séparé reçoit les notifications Microsoft Graph.

Il doit seulement :

- répondre à la validation initiale de Microsoft;
- reconnaître la souscription;
- valider le `clientState`;
- placer une demande de synchronisation dans Azure Service Bus;
- retourner rapidement `200 OK` ou `202 Accepted`.

Il ne télécharge aucun document et ne crée aucun embedding.

### AssistantCore.Ingestion.Worker

Le worker séparé :

- consomme les messages Azure Service Bus;
- exécute les synchronisations;
- traite les documents;
- renouvelle les souscriptions Microsoft Graph;
- effectue les vérifications périodiques de sécurité;
- reprend les travaux qui ont échoué temporairement.

### AssistantCore.ExternalServices

Ce projet contient les clients responsables des appels externes :

- client Microsoft Graph;
- client Azure AI Search;
- client du fournisseur d’embeddings;
- client Azure Service Bus.

Les types propres aux SDK externes ne doivent pas remonter dans la couche
Application.

### AssistantCore.Repository

La persistence conserve la configuration et les états de synchronisation. Elle
ne conserve pas le texte complet des documents.

<a id="m365-sharepoint-onboarding"></a>
## Connexion du tenant Microsoft 365

### But du consentement

Le consentement administrateur autorise le Worker AssistantCore à obtenir un
token technique Microsoft Graph pour le tenant du client. Le Worker peut alors
lire les sources SharePoint et OneDrive que l'organisation a choisi d'activer,
puis injecter leur contenu dans Azure AI Search.

Sans ce consentement, Azure AI Search reste disponible, mais le Worker ne peut
obtenir aucun contenu Microsoft 365 :

```text
Microsoft Graph refuse l'accès au Worker
  -> aucune source SharePoint ou OneDrive ne peut être lue
  -> aucun document Microsoft 365 ne peut être traité
  -> aucun passage Microsoft 365 ne peut être injecté dans Azure AI Search
```

Le consentement n'ajoute pas automatiquement tous les sites du tenant. Il
autorise les appels Graph. AssistantCore attend ensuite que l'administrateur
choisisse un ou plusieurs sites. Ce choix ajoute automatiquement les
bibliothèques et les listes compatibles de chaque site confirmé.

### Flow détaillé de connexion

```text
Admin connecté
  -> POST /api/microsoft365/consent
  -> Controller -> Dispatcher -> Handler -> Service
  -> création d'une connexion PendingConsent et d'un state protégé
  <- authorizationUrl Microsoft

Admin chez Microsoft
  -> accepte les permissions
  -> GET /api/microsoft365/consent/callback?tenant=...&admin_consent=True&state=...
  -> Controller -> Dispatcher -> Handler -> Service
  -> validation du state, du consentement et du tenant
  -> obtention d'un token applicatif et vérification du tenant auprès de Graph
  -> connexion Active
  -> redirection vers la page de confirmation du frontend
  -> Worker autorisé à obtenir un token Graph et à lire les sources activées
```

#### Démarrer le consentement

L'administrateur authentifié appelle :

```http
POST /api/microsoft365/consent
Authorization: Bearer <JWT AssistantCore>
```

Le controller envoie une commande au `IDispatcher` et ne contient aucune
logique métier. Le handler appelle uniquement le service applicatif de
connexion Microsoft 365.

Le service retrouve l'utilisateur et son organisation depuis le JWT. Aucun
`organizationId` n'est accepté depuis la requête. Il vérifie ensuite que le
jeton contient `AssistantCore.Access` et `TenantAdmin`; sinon, il retourne
`403 Forbidden`. Le rôle indicatif conservé en base n'autorise pas cette action.

Le service génère un `state` contenant l'organisation, un nonce aléatoire et
une expiration courte. Le `state` est chiffré et signé. Seule son empreinte est
persistée afin de reconnaître le callback sans conserver sa valeur brute.

La connexion passe à l'état suivant :

```text
Status: PendingConsent
TenantId: inconnu
Consentement: pas encore accordé
```

Le client Microsoft construit ensuite une URL vers l'endpoint multitenant de
consentement administrateur. La permission statique `/.default` demande les
permissions applicatives Microsoft Graph configurées sur l'inscription. Le
controller retourne cette URL au frontend :

```json
{
  "authorizationUrl": "https://login.microsoftonline.com/organizations/..."
}
```

Le frontend redirige le navigateur vers cette URL. À la fin de cette première
opération, aucune source Microsoft 365 ne peut encore être lue.

#### Terminer le consentement

Après la décision de l'administrateur, Microsoft rappelle AssistantCore :

```http
GET /api/microsoft365/consent/callback?tenant=<tenant-id>&admin_consent=True&state=<state>
```

Le service valide la signature, l'expiration, l'organisation et l'usage unique
du `state`. Il vérifie ensuite que Microsoft indique explicitement
`admin_consent=True` et que le tenant retourné est un identifiant valide. La
valeur `tenant` du callback n'est jamais considérée comme une preuve à elle
seule : l'adapter Infrastructure demande un token applicatif à ce tenant avec
le flow `client_credentials`, appelle Microsoft Graph avec ce token et vérifie
que Graph retourne le même tenant. Le service refuse aussi ce tenant s'il est
déjà associé à une autre organisation.

Un consentement accordé ne prouve pas à lui seul que les permissions requises
par le connecteur sont réellement utilisables : elles peuvent avoir été
retirées après coup, ou l'administrateur peut avoir accordé un consentement
partiel. Avant d'activer la connexion, le service effectue donc un appel Graph
représentatif (`GET /v1.0/sites?search=*&$top=1`) avec le token applicatif
obtenu. Cet appel ne lit aucune donnée utile : il sert uniquement à vérifier
que `Sites.Read.All` est effectivement utilisable. Un refus `403` de Microsoft
Graph interrompt l'activation; tout autre échec technique est traité comme une
validation impossible.

Chacun de ces refus est exposé à l'appelant via un code d'erreur métier stable
plutôt qu'un simple message :

| Code | Situation |
| --- | --- |
| `admin_consent_incomplete` | `state` absent, invalide, expiré ou déjà utilisé |
| `admin_consent_refused` | Microsoft indique une erreur ou `admin_consent=False` |
| `wrong_tenant` | le tenant validé est déjà connecté à une autre organisation |
| `admin_consent_validation_failed` | l'acquisition du token applicatif ou l'appel Graph de vérification échoue techniquement |
| `missing_required_permissions` | l'appel Graph représentatif est refusé (`403`) : permissions absentes ou retirées |

Après un retour valide, la connexion devient :

```text
Status: Active
TenantId: tenant Microsoft validé
ConsentValidatedAt: date du callback
```

Le token technique est protégé et n'est jamais écrit dans les logs. Le Worker
dispose alors de la condition nécessaire pour demander un token Graph et lire
les contenus des sites qui seront choisis ensuite.

Après une validation réussie, l'API ne présente pas de réponse JSON à
l'administrateur. Elle retourne une redirection HTTP vers une URL frontend fixe
et configurée par l'environnement. La page onPremia confirme que Microsoft 365
est connecté et met clairement en évidence le choix du site comme prochaine
étape. L'identifiant du tenant, le token technique et le `state` ne sont
jamais ajoutés à cette URL.

Si le consentement est refusé, expire ou ne peut pas être validé, le callback
redirige vers une page frontend d'échec. Le détail technique est conservé dans
les journaux du backend, tandis que l'interface indique seulement comment
recommencer avec un compte administrateur approprié.

Le callback ne découvre pas encore les sites, ne télécharge aucun document et
n'écrit rien dans Azure AI Search. Ces traitements appartiennent aux étapes de
découverte et de synchronisation.

#### Consentement dans l'environnement local simulé

La SPA utilise le même bouton, le même endpoint et le même traitement de retour
en environnement local et en environnement connecté.

En environnement connecté, `authorizationUrl` pointe vers Microsoft Entra.
En environnement `Local`, elle pointe vers une page HTML servie par WireMock.
Cette page est clairement identifiée comme une simulation locale et permet de
choisir `Accepter` ou `Refuser`.

```text
SPA
  -> POST /api/microsoft365/consent
  <- authorizationUrl
  -> environnement connecté : page Microsoft Entra
  -> environnement Local : page de consentement WireMock
  -> callback AssistantCore identique
  -> validation du tenant avec Microsoft Graph réel ou simulé
  -> redirection vers la même page SPA
```

WireMock ne contourne pas le backend. Il simule uniquement les réponses du
fournisseur Microsoft : consentement, token applicatif, organisation et
ressources Graph. Le `state`, le callback, les validations, la persistence et
les changements d'état restent exécutés par AssistantCore.

Le JWT local est conservé dans le `sessionStorage` du navigateur afin de
survivre à la redirection vers la page simulée. Il est supprimé lors de la
déconnexion ou à la fermeture de l'onglet. Ce comportement est limité au mode
`Local`; le mode connecté continue d'utiliser le cache MSAL.

#### Révoquer la connexion

Un administrateur peut retirer l'accès d'AssistantCore avec :

```http
DELETE /api/microsoft365/connections/{connectionId}
Authorization: Bearer <JWT AssistantCore>
```

Le controller transmet une commande au `IDispatcher`. Le handler appelle le
service de connexion, qui retrouve l'organisation depuis le JWT et exige le
claim `TenantAdmin`. Aucun `organizationId` n'est accepté dans la requête.

Le service recherche la connexion avec son identifiant et l'organisation
courante. Une connexion appartenant à une autre organisation est traitée comme
absente. Lorsqu'elle est trouvée, la connexion et son connecteur deviennent
inactifs et le jeton technique conservé est supprimé :

```text
Admin connecté
  -> DELETE /api/microsoft365/connections/{connectionId}
  -> Controller -> Dispatcher -> Handler -> Service
  -> connexion Revoked
  -> connecteur Inactive
  -> suppression du jeton technique
  -> Worker refuse les nouveaux traitements
```

Un nouvel appel de consentement est nécessaire pour remettre cette connexion
en service. Une connexion révoquée ne peut pas passer directement à `Active`
ou `Error`.

Le travail demandé au client doit être limité à une séance d’onboarding.

L’administrateur :

1. ouvre l’écran de connexion Microsoft 365;
2. se connecte avec un compte administrateur;
3. accepte les permissions présentées par Microsoft;
4. choisit les sites autorisés;
5. confirme l’activation.

AssistantCore effectue ensuite automatiquement :

- la validation du tenant;
- l’enregistrement du connecteur;
- la découverte des bibliothèques;
- la création des souscriptions;
- la synchronisation initiale;
- les synchronisations suivantes.

Le client ne doit pas fournir de secret, certificat ou token à AssistantCore.

<a id="m365-sharepoint-permissions"></a>
## Permissions Microsoft nécessaires

Le connecteur utilise les permissions applicatives étendues nécessaires aux
webhooks, aux requêtes delta et à la lecture des autorisations :

- `Files.Read.All`;
- `Sites.Read.All`;
- `Organization.Read.All`, pour vérifier que le token applicatif correspond
  bien au tenant revenu du consentement;
- la permission minimale nécessaire pour lire les groupes du membre lors
  d’une recherche.

Ces permissions demandent un consentement administrateur.

Elles donnent une visibilité étendue dans le tenant. Ce choix est assumé pour
permettre la découverte, les webhooks, les deltas et la synchronisation rapide
des changements de permissions. AssistantCore doit donc conserver sa propre
liste de sites activés et refuser toute découverte, ingestion ou indexation
provenant d'un site qui n'a pas été explicitement autorisé dans onPremia.

`Sites.Selected` n'est pas utilisé dans ce flow. Les écrans d'administration
doivent expliquer clairement la portée des permissions demandées et rappeler
que la sélection réalisée dans onPremia contrôle les sources effectivement
traitées.

<a id="m365-sharepoint-sources"></a>
## Sites, bibliothèques et listes autorisés

### Onboarding et écran d'administration Microsoft 365

Après l'authentification, la SPA vérifie la progression de la configuration de
l'organisation avant d'ouvrir le chat :

```http
GET /api/microsoft365/onboarding
Authorization: Bearer <JWT AssistantCore>
```

La réponse indique si le membre est administrateur, si le consentement est
valide, si un site a été choisi, si du contenu est disponible et si
l'onboarding est terminé. Elle ne contient aucun secret, token technique ou
identifiant de tenant.

Pour une organisation qui n'est pas encore prête, un administrateur suit un
assistant linéaire en deux étapes :

1. autoriser l'accès de l'organisation à Microsoft 365;
2. choisir au moins un site SharePoint.

Les deux étapes restent visibles pendant tout le parcours. Seule l'étape
courante est interactive. Les étapes suivantes sont grisées et expliquent ce
qui doit être terminé avant de devenir disponibles. Cette présentation empêche
de sauter une validation tout en laissant comprendre l'ensemble du parcours.
Une étape terminée est atténuée et l'étape courante est mise en évidence. Au
retour du consentement, une courte animation guide le regard vers le choix du
site sans bloquer l'utilisateur.

Le retour du consentement, qu'il soit accepté ou refusé, affiche son résultat
dans le même assistant. Un consentement valide active immédiatement l'étape de
sélection du site. Un refus conserve l'étape d'autorisation active et permet de
recommencer.

L'administrateur peut cocher et décocher plusieurs sites avant de confirmer sa
sélection. La confirmation ajoute automatiquement toutes les bibliothèques et
toutes les listes compatibles des sites retenus. Le bouton permettant d'accéder
au chat apparaît dès qu'au moins un site a été préparé et qu'au moins une de ses
bibliothèques ou listes est activée pour l'indexation; il n'existe aucune
troisième action manuelle obligatoire.
Lors des connexions suivantes, une organisation déjà
configurée arrive directement dans le chat. Un membre non administrateur voit
un écran d'attente clair tant que la configuration doit encore être terminée
par un administrateur.

Cette règle n'est pas seulement une présentation frontend : tant que
l'onboarding n'est pas terminé (consentement valide, au moins un site
sélectionné et au moins une source enfant activée), le backend refuse
`403 Forbidden` avec le code métier
`tenant_admin_required` à tout appel d'un membre qui ne possède pas le rôle
Entra `TenantAdmin`, y compris `authenticateUser` lui-même. Un membre avec
`TenantAdmin` reste toujours admis, que la configuration soit terminée ou non.
Une fois l'onboarding termine, `TenantAdmin` cesse d'etre requis pour les
membres standards : voir [Authenticate User](../authentification/authenticate-user.md#auth-admission-policy)
pour la regle complete de derivation du role et de la politique d'admission.

Après l'onboarding, un membre dont le jeton contient `TenantAdmin` retrouve le même écran
depuis le menu de la SPA pour retirer ou ajouter des contenus. Ce réglage est
facultatif et ne bloque pas le chat. Le backend continue de refuser les actions
administratives aux autres membres.

La SPA n'utilise aucune donnée Microsoft 365 codée en dur. Elle appelle les
mêmes endpoints dans tous les environnements. En mode local, le backend reçoit
les réponses Graph de WireMock; en mode connecté, il reçoit celles de Microsoft.

### Découvrir les sites disponibles

Après le consentement, l'administrateur appelle :

```http
GET /api/microsoft365/sites
Authorization: Bearer <JWT AssistantCore>
```

Le traitement suit le chemin obligatoire :

```text
Controller
  -> IDispatcher
  -> QueryHandler
  -> service applicatif
  -> interface applicative
  -> adapter Infrastructure
  -> client AssistantCore.ExternalServices
  -> GET Microsoft Graph /v1.0/sites
```

Le service vérifie que le membre courant est administrateur et que son
organisation possède une connexion Microsoft 365 active. Il obtient ensuite
un token technique pour le tenant et demande les sites à Microsoft Graph.
Toutes les pages `@odata.nextLink` sont suivies.

La réponse contient l'identifiant, le nom, l'URL et l'indication qu'un site est
déjà sélectionné dans onPremia. La découverte seule ne crée aucune source,
n'active aucune indexation et ne télécharge aucun contenu.

L'administrateur sélectionne un site avec l'endpoint existant :

```http
POST /api/microsoft365/sites/{siteId}
```

Le backend valide de nouveau le site auprès de Graph avant de l'enregistrer.
Il découvre ensuite ses bibliothèques et ses listes compatibles, les active et
crée le travail nécessaire à leur première synchronisation. La SPA considère
alors l'onboarding comme terminé sans demander une sélection supplémentaire,
mais uniquement si au moins une source enfant est effectivement activée. Si la
découverte s'interrompt après l'enregistrement du site, la même sélection peut
être relancée : les lignes valides sont réutilisées et les travaux initiaux ou
souscriptions manquants sont recréés sans doublon.

Voir un site dans la liste ne l'ajoute pas à onPremia. Seul le choix explicite
de l'administrateur autorise ses contenus compatibles.

Chaque site possède un état :

- `Discovered` : trouvé mais pas encore autorisé;
- `Enabled` : autorisé et synchronisé;
- `Disabled` : désactivé volontairement;
- `Error` : configuration ou accès invalide.

Un nouveau site choisi est ajouté comme `Enabled`. Ses contenus compatibles
sont activés dans la même opération.

Lorsqu’un site est activé :

1. lire ses bibliothèques;
2. lire ses listes avec Microsoft Graph;
3. enregistrer les bibliothèques et listes comme sources découvertes;
4. activer automatiquement chaque contenu compatible;
5. créer une synchronisation initiale pour chaque contenu activé;
6. créer une souscription par bibliothèque ou liste lorsque cela est supporté.

Ces opérations sont idempotentes. Une source déjà activée est tout de même
réconciliée afin de réparer un travail initial absent ou définitivement échoué.
Le worker peut aussi reprendre une synchronisation restée `Running` après un
arrêt lorsque son lease a expiré. Il n'est jamais nécessaire de vider la base
pour reprendre ce flow.

Le choix du site autorise donc tout son contenu compatible, y compris les
listes non masquées qui ne sont ni des listes système ni des bibliothèques de
documents. L'administrateur doit choisir un site approprié pour l'organisation.
Il peut ensuite retirer individuellement un contenu depuis l'écran
d'administration.

### Découvrir les listes d’un site

Le client Microsoft Graph de `AssistantCore.ExternalServices` appelle :

```http
GET https://graph.microsoft.com/v1.0/sites/{siteId}/lists
```

Il suit chaque `@odata.nextLink` jusqu’à la dernière page. Les bibliothèques de
documents, les listes système et les listes supprimées ne sont pas proposées
comme listes de contenu. Chaque autre liste est enregistrée avec l’état
`Discovered`.

Exemple de source découverte :

```json
{
  "siteId": "contoso.sharepoint.com,site-123,web-456",
  "listId": "8f25d1d1-7e5c-4a3f-8d3e-4ed78a4a1720",
  "displayName": "Demandes d'achat",
  "webUrl": "https://contoso.sharepoint.com/sites/finance/Lists/Achats",
  "status": "Discovered",
  "isIndexed": false
}
```

### Endpoints administratifs

Ces endpoints sont réservés aux administrateurs de l’organisation connectée :

```http
POST /api/microsoft365/sites/{siteId}
GET /api/microsoft365/sites/{siteId}/drives
PATCH /api/microsoft365/sites/{siteId}/drives/{driveId}
GET /api/microsoft365/sites/{siteId}/lists
PATCH /api/microsoft365/sites/{siteId}/lists/{listId}
```

Le premier endpoint valide le site auprès de Microsoft Graph, l'enregistre et
active automatiquement ses contenus compatibles. Les endpoints `GET` affichent
ensuite les contenus disponibles. Les endpoints `PATCH` permettent à
l'administrateur de retirer un contenu ou de l'ajouter de nouveau. Le Worker
prend ensuite le travail en charge :

```text
bibliothèque activée
  -> delta Graph
  -> téléchargement du DOCX
  -> résolution de l'ACL
  -> extraction et passages déterministes
  -> embeddings
  -> mergeOrUpload Azure AI Search
  -> isAvailable = true après confirmation
```

Le DOCX brut n'est pas conservé dans Azure AI Search. Chaque passage contient
le texte, le vecteur, la version, l'URL SharePoint et les champs d'autorisation.

Le `GET` retourne les listes découvertes avec leur état. Le `PATCH` accepte
uniquement l’activation ou la désactivation de l’indexation :

```json
{
  "isIndexed": true
}
```

Le controller obtient l’organisation depuis l’identité authentifiée et jamais
depuis le corps ou l’URL. Le traitement suit obligatoirement
`Controller -> IDispatcher -> Handler -> service applicatif -> repository`.
Une activation est refusée si le site, la liste ou le connecteur n’appartient
pas à l’organisation courante.

Après une activation réussie, le backend crée une demande de synchronisation
initiale. Après une désactivation, il arrête la souscription, interdit les
nouvelles synchronisations et supprime de l’index tous les passages de cette
liste.

<a id="m365-sharepoint-content-types"></a>
## Types de contenus Microsoft 365

La première version distingue les sources suivantes :

| Source | Contenu indexé |
| --- | --- |
| Bibliothèque SharePoint | fichiers et dossiers |
| Pages SharePoint | titre, texte et contenu utile des composants |
| Listes SharePoint | titre de la liste, colonnes et valeurs des éléments |
| OneDrive professionnel | fichiers du OneDrive individuel autorisé |
| Archive | fichiers supportés contenus dans l’archive |

### Tous les fichiers sauf audio et vidéo

« Tous les fichiers sauf audio et vidéo » signifie que le pipeline accepte le
fichier et tente de déterminer comment en extraire une information utile.

Le résultat peut être :

- texte extrait directement;
- texte extrait avec OCR;
- données structurées transformées en texte lisible;
- contenu de fichiers extraits d’une archive;
- métadonnées seulement lorsqu’aucun contenu lisible n’existe.

Les fichiers audio et vidéo sont ignorés avant le téléchargement lorsque leur
type est connu.

Les fichiers exécutables, bibliothèques, images disque et autres binaires sans
contenu documentaire sont enregistrés comme `NoIndexableContent`.

### Formats Office

Le traitement doit couvrir :

- Word : DOC, DOCX et DOCM;
- Excel : XLS, XLSX et XLSM;
- PowerPoint : PPT, PPTX et PPTM.

Pour Excel, conserver les noms des feuilles et transformer les cellules utiles
en texte structuré. Les formules peuvent être indexées avec leur dernière
valeur enregistrée. Le worker ne doit pas recalculer un classeur.

L’indexation textuelle sert à retrouver un classeur ou une information
ponctuelle. Lorsqu’une question exige un agrégat ou un filtre sur toutes les
lignes, l’outil `AnalyzeSpreadsheet` télécharge le classeur autorisé et utilise
un analyseur déterministe. Le fichier est localisé grâce à son entrée indexée,
mais les calculs ne dépendent pas du nombre de passages retournés par la
recherche sémantique et n’envoient pas le fichier complet au modèle.

Pour PowerPoint, conserver le numéro de diapositive, le titre, le texte et les
notes lorsque celles-ci sont disponibles.

Pour Word, conserver les titres, paragraphes, tableaux et numéros de page
lorsqu’ils peuvent être déterminés.

### Images et documents scannés

Les images et PDF scannés passent par un service OCR.

Le texte obtenu doit conserver autant que possible :

- le numéro de page;
- l’ordre de lecture;
- les titres;
- les tableaux;
- la langue détectée.

Un résultat OCR vide est enregistré comme `NoIndexableContent`.

<a id="m365-sharepoint-persistence"></a>
## Données conservées dans la base

La persistence doit représenter au minimum :

### Connexion Microsoft 365

- organisation interne;
- identifiant du tenant Microsoft;
- état du consentement;
- date de dernière validation;
- état général du connecteur.

Les états persistés par le socle d’ingestion sont :

| Élément | États | Signification |
| --- | --- | --- |
| Connexion | `PendingConsent`, `Active`, `Error`, `Revoked` | Consentement en cours, connexion utilisable, erreur contrôlée ou accès révoqué. |
| Source | `Discovered`, `Enabled`, `Disabled`, `Error` | Source trouvée, autorisée, désactivée ou invalide. |
| Souscription | `Pending`, `Active`, `RenewalRequired`, `Error`, `Revoked`, `Expired` | Cycle de vie d’un webhook Microsoft Graph. |
| Synchronisation | `Pending`, `Running`, `Succeeded`, `TemporaryFailure`, `PermanentFailure`, `Cancelled` | Cycle de vie d’une exécution d’ingestion. |

Une seule connexion Microsoft 365 existe par organisation. Un tenant Microsoft
ne peut être associé qu’à une seule organisation AssistantCore. Le démarrage du
consentement conserve uniquement l’empreinte du `state`; sa valeur signée et
expirante revient dans le callback et ne peut être consommée qu’une fois.

### Site SharePoint

- identifiant Microsoft du site;
- URL;
- nom affiché;
- état;
- date de découverte;
- date d’activation.

### Bibliothèque

- identifiant du `drive`;
- site parent;
- nom;
- état;
- `deltaLink`;
- dernière synchronisation réussie;
- prochaine vérification prévue;
- dernière erreur.

### Liste SharePoint

- organisation et connecteur parents;
- identifiant Microsoft du site;
- identifiant Microsoft de la liste;
- nom affiché et URL;
- état `Discovered`, `Enabled`, `Disabled` ou `Error`;
- indicateur d’indexation;
- empreinte du schéma de colonnes;
- indicateur persistant demandant le retraitement des éléments lorsque cette
  empreinte change après son initialisation;
- `deltaLink` opaque retourné par Microsoft Graph;
- dernière synchronisation réussie;
- dernière tentative de synchronisation;
- identifiant et expiration du lease de synchronisation en cours;
- prochaine réconciliation prévue;
- dernière erreur.

La combinaison `organisation + connecteur + siteId + listId` est unique.

### Élément de liste

- organisation, site et liste parents;
- `listItemId` Microsoft;
- titre calculé;
- URL;
- `eTag` ou version;
- dates de création et de modification;
- empreinte des permissions;
- nombre de passages indexés;
- état de traitement et dernière erreur.

La combinaison `organisation + siteId + listId + listItemId` est unique. Elle
sert aussi de base à la clé déterministe des passages Azure AI Search.

### Souscription

- identifiant de souscription Microsoft;
- bibliothèque ou liste surveillée;
- date d’expiration;
- `clientState` protégé;
- dernier renouvellement;
- état.

### Document

- organisation;
- site;
- bibliothèque;
- identifiant Microsoft du fichier;
- nom;
- URL;
- type de fichier;
- `eTag` ou version;
- date de modification;
- nombre de passages indexés;
- état de traitement;
- dernière erreur;
- dernière date d’indexation.

Les états de traitement possibles doivent distinguer :

- en attente;
- en traitement;
- indexé;
- ignoré;
- en erreur temporaire;
- en erreur permanente;
- supprimé.

<a id="m365-sharepoint-webhooks"></a>
## Création des webhooks

Après l’activation d’une bibliothèque, le gestionnaire de souscriptions appelle
Microsoft Graph pour surveiller :

```text
/drives/{driveId}/root
```

Après l’activation d’une liste, il surveille :

```text
/sites/{siteId}/lists/{listId}
```

La souscription contient :

- l’URL HTTPS publique du webhook;
- la ressource surveillée;
- sa date d’expiration;
- un `clientState` aléatoire et secret.

Pour recevoir aussi les événements de sécurité pris en charge sur les
bibliothèques et OneDrive, la création d'une souscription `/drives/...` utilise
l’en-tête :

```http
Prefer: includesecuritywebhooks
```

Cet en-tête n'est pas envoyé pour une souscription de liste
`/sites/{siteId}/lists/{listId}`.

### Validation initiale

Microsoft appelle :

```http
POST /webhooks/microsoft-graph?validationToken=<token>
```

L’endpoint retourne le token décodé :

```http
HTTP 200 OK
Content-Type: text/plain

<token>
```

Cette réponse doit être envoyée sans lancer de traitement long.

### Notification normale

Pour chaque notification :

1. retrouver la souscription avec `subscriptionId`;
2. vérifier que son tenant correspond;
3. comparer le `clientState`;
4. ignorer les notifications invalides;
5. créer une demande `SynchronizeDrive` ou `SynchronizeList` selon la source;
6. retourner `202 Accepted`.

Le webhook ne fait jamais confiance à un `organizationId` fourni dans la
notification. Il retrouve l’organisation depuis la souscription enregistrée.

### Protection du `clientState`

Le `clientState` original n'est jamais stocké : Microsoft Graph ne l'exige
jamais pour renouveler une souscription (seules l'expiration et l'URL de
notification sont renvoyées lors d'un renouvellement), donc seule sa capacité
à être vérifié est nécessaire. La base ne conserve qu'un HMAC-SHA256 du
`clientState`, calculé avec une clé secrète qui n'est jamais stockée en SQL
(comme `ClientSecret`, via la configuration/Key Vault). La comparaison à la
réception d'une notification se fait en temps constant
(`CryptographicOperations.FixedTimeEquals`) pour ne pas exposer d'information
via le temps de réponse. Aucune valeur de `clientState` n'est jamais journalisée.

Un `clientState` protégé est irréversible par construction : il ne peut donc
pas être migré vers un nouvel algorithme ou une nouvelle clé sans connaître la
valeur d'origine. Si l'algorithme ou la clé change, les souscriptions
existantes sont marquées pour être recréées proprement (suppression de
l'abonnement Microsoft Graph existant puis nouvelle souscription avec un
nouveau `clientState`) par le traitement planifié de renouvellement, qui
déclenche aussi une réconciliation complète pour couvrir toute notification
manquée pendant la bascule.

<a id="m365-sharepoint-webhook-renewal"></a>
## Renouvellement des webhooks

Les souscriptions Microsoft Graph expirent.

Un traitement planifié du worker :

1. recherche les souscriptions qui approchent de leur expiration;
2. tente de les renouveler;
3. enregistre la nouvelle expiration;
4. recrée une souscription absente ou invalide;
5. demande une synchronisation delta après une interruption.

Le renouvellement doit avoir lieu plusieurs jours avant l’expiration. Une
alerte doit être produite si une souscription reste impossible à renouveler.

<a id="m365-sharepoint-initial-sync"></a>
## Synchronisation initiale

Lors de la première synchronisation d’une bibliothèque :

1. appeler la requête delta sans ancien token;
2. lire toutes les pages `@odata.nextLink`;
3. enregistrer chaque fichier supporté comme travail à traiter;
4. ignorer les dossiers comme documents, mais continuer leur parcours;
5. enregistrer les suppressions reçues;
6. conserver le `@odata.deltaLink` final seulement lorsque les changements ont
   été enregistrés durablement.

La synchronisation initiale doit pouvoir être relancée sans créer de doublons.

Lors de la première synchronisation d’une liste :

1. charger ses colonnes avec
   `GET /sites/{siteId}/lists/{listId}/columns`;
2. construire la sélection des colonnes indexables;
3. appeler
   `GET /sites/{siteId}/lists/{listId}/items/delta?$expand=fields`;
4. suivre chaque `@odata.nextLink` sans charger toute la liste en mémoire;
5. créer un travail `ProcessListItem` pour chaque élément créé ou modifié;
6. créer un travail `DeleteListItem` pour chaque élément portant la facette
   `deleted`;
7. enregistrer le `@odata.deltaLink` seulement lorsque toutes les pages et tous
   les travaux ont été enregistrés durablement.

Une seule page est conservée en mémoire à la fois. Avant de demander la page
suivante, le worker enregistre durablement les travaux de la page courante.
Un travail normal conserve les identifiants, l’`eTag`, les dates, l’URL et les
champs nécessaires; un travail de suppression conserve seulement les
identifiants requis pour nettoyer l’index. Les champs ne sont jamais écrits
dans les logs. Une clé déterministe empêche un rejeu de créer un doublon.

Exemple : si la deuxième page Graph échoue, le nouveau `deltaLink` n’est pas
enregistré. La prochaine exécution reprend depuis le dernier point confirmé et
les clés déterministes empêchent les doublons.

<a id="m365-sharepoint-delta-sync"></a>
## Synchronisation des changements

Après la synchronisation initiale, le worker réutilise le `deltaLink`.

Le worker doit gérer :

- un fichier créé;
- un fichier modifié;
- un fichier renommé;
- un fichier déplacé;
- un fichier supprimé;
- plusieurs apparitions du même fichier;
- plusieurs pages de résultats;
- un token delta expiré;
- une demande Microsoft de recommencer une synchronisation complète.

Le webhook sert à démarrer rapidement cette synchronisation. Une tâche
planifiée exécute aussi périodiquement le delta afin de récupérer un changement
dont la notification aurait été perdue.

Une seule synchronisation d’une bibliothèque peut s’exécuter à la fois.
La même règle s’applique à une liste : une seule synchronisation peut modifier
son checkpoint à la fois.

Pour une liste, le webhook ne contient pas toutes les nouvelles valeurs. Il
réveille uniquement la synchronisation delta. Une réconciliation planifiée
relance aussi le delta afin de couvrir une notification perdue.

<a id="m365-sharepoint-admin-reindex"></a>
## Réindexation administrative d’un client

L’équipe Synaptix doit pouvoir relancer une indexation complète depuis sa
future interface d’administration interne. Cette opération sert notamment à
reprendre les documents restés en échec après une correction du connecteur,
des permissions Microsoft 365 ou du pipeline d’ingestion.

Cette capacité n’est jamais exposée dans l’application du client. Un
administrateur du tenant client, même autorisé à configurer Microsoft 365, ne
peut pas la déclencher. Seul un opérateur Synaptix authentifié et autorisé dans
l’interface interne peut agir sur une autre organisation.

### Déclenchement

L’opérateur Synaptix :

1. ouvre la fiche d’une organisation cliente dans l’interface interne;
2. consulte les sources SharePoint de cette organisation;
3. clique sur `Relancer l’indexation SharePoint`;
4. confirme l’opération;
5. reçoit l’identifiant et l’état initial de la réindexation.

La commande vise toutes les bibliothèques SharePoint activées pour
l’indexation dans cette organisation. L’interface affiche clairement le nom du
client et le nombre de bibliothèques concernées avant la confirmation.

La requête interne contient l’identifiant de l’organisation. Le backend
retrouve lui-même ses connexions et ses sources : il n’accepte pas des
identifiants de bibliothèques appartenant à une autre organisation. Une
réponse acceptée retourne un identifiant d’opération et l’état `Pending`.

### Flow backend et worker

La requête suit le chemin obligatoire :

```text
Interface d’administration Synaptix
  -> Controller interne
  -> IDispatcher
  -> CommandHandler
  -> Service applicatif de réindexation
  -> Persistence
  -> Worker d’ingestion Microsoft 365
```

Le controller vérifie l’authentification interne et transmet uniquement la
commande au dispatcher. Le handler orchestre l’appel au service applicatif. Le
service :

1. vérifie que l’opérateur possède le droit interne de réindexer les contenus;
2. charge l’organisation, sa connexion Microsoft 365 active et ses
   bibliothèques activées pour l’indexation;
3. refuse la demande si une réindexation complète est déjà en cours pour cette
   organisation;
4. crée une opération suivie et une synchronisation complète `Pending` pour
   chaque bibliothèque admissible;
5. enregistre l’identité de l’opérateur, l’organisation ciblée, la date et le
   motif facultatif;
6. retourne immédiatement l’identifiant de l’opération sans attendre le
   traitement des documents.

Le worker réclame ensuite chaque synchronisation. Il relit toute la
bibliothèque depuis Microsoft Graph sans dépendre de l’ancien `deltaLink`. Il
crée un nouveau travail pour chaque version courante, y compris lorsqu’un
travail portant le même document ou la même version avait terminé en
`PermanentFailure`. Les clés de déduplication distinguent la nouvelle opération
tout en empêchant les doublons à l’intérieur de celle-ci.

Chaque document repasse par le pipeline normal : téléchargement, extraction,
résolution des permissions, découpage, embeddings et écriture dans Azure AI
Search. Les permissions sont donc recalculées à partir de l’état courant dans
SharePoint. Un document ne devient jamais visible grâce à une ancienne ACL.

Le pipeline garde son contrôle de version : un document déjà indexé dans sa
version courante et toujours disponible n’est pas téléchargé de nouveau et ne
consomme aucun embedding. La reprise concentre donc son travail sur les
documents absents de l’index, notamment ceux dont un ancien travail avait
échoué. Les changements de permissions sur un document inchangé restent traités
par la réconciliation planifiée des ACL.

La réindexation n’efface pas l’index au démarrage. Les passages valides restent
disponibles jusqu’au remplacement réussi de leur document. À la fin du
parcours, les contenus qui n’existent plus dans SharePoint sont retirés. Le
nouveau checkpoint delta devient actif uniquement lorsque la synchronisation
complète a enregistré toutes ses pages durablement.

### Suivi et résultat

L’interface interne peut relire l’état de l’opération et affiche au minimum :

- `Pending`, `Running`, `Succeeded`, `TemporaryFailure` ou
  `PermanentFailure`;
- le nombre de bibliothèques terminées et le nombre total;
- les nombres de documents découverts, traités, ignorés et en échec;
- les heures de demande, de début et de fin;
- un code d’erreur exploitable sans contenu documentaire sensible.

Une reprise technique du même message ou du même lot reste idempotente. Une
erreur temporaire est retentée par le worker. Une erreur permanente sur un
document est visible dans le bilan sans empêcher les autres documents ou
bibliothèques de terminer.

Avant l’opération, certains documents du client peuvent être absents ou
obsolètes dans Azure AI Search, notamment après un ancien échec permanent.
Après une opération réussie, toutes les versions courantes et autorisées des
bibliothèques activées ont été retraitées, les documents supprimés ont été
retirés et la synchronisation delta normale peut reprendre depuis le nouveau
checkpoint.

### Limites

La première version de cette commande :

- agit sur toutes les bibliothèques activées d’une organisation, pas sur un
  dossier ou un document isolé;
- ne réindexe pas les listes SharePoint ni les OneDrive individuels;
- ne permet pas à un administrateur client de déclencher l’opération;
- ne permet pas deux réindexations complètes simultanées pour la même
  organisation;
- ne contourne jamais les permissions Microsoft 365 ni les règles de sécurité
  d’Azure AI Search.

<a id="m365-sharepoint-worker"></a>
## Traitement par le worker

Le projet `AssistantCore.Ingestion.Worker` possède son propre démarrage et sa
propre injection de dépendances. Le socle du ticket 45 permet de vérifier une
connexion fournie par son identifiant interne au démarrage. Seule une connexion
`Active` est acceptée; les états `PendingConsent`, `Error` et `Revoked` sont
refusés avant tout appel à Microsoft.

Cette vérification est volontairement minimale. Elle confirme que le processus
séparé démarre, accède à la persistence et applique l'état de la connexion. La
consommation des files Service Bus et la synchronisation réelle sont ajoutées
par les tickets suivants.

Utiliser au minimum deux files de travail :

```text
sharepoint-drive-sync
sharepoint-document-process
sharepoint-list-sync
sharepoint-list-item-process
```

La première demande de découvrir les changements d’une bibliothèque.

La seconde demande de traiter un document précis.

La troisième demande de lire les changements d’une liste. La quatrième demande
de transformer ou supprimer un élément de liste précis.

Chaque message doit être petit. Il contient des identifiants et jamais le texte
complet du document.

Le worker doit être idempotent :

- recevoir deux fois le même message ne crée pas de doublon;
- retraiter la même version produit les mêmes clés;
- une ancienne version ne remplace pas une version plus récente;
- une suppression répétée reste sans effet secondaire.

Les messages qui échouent plusieurs fois sont placés dans une file d’erreurs
afin de pouvoir être examinés.

<a id="m365-sharepoint-extraction"></a>
## Téléchargement et extraction

Pour chaque fichier :

1. vérifier que sa version n’est pas déjà indexée;
2. vérifier son extension et sa taille;
3. lire ses permissions;
4. refuser l’indexation si les permissions ne sont pas comprises;
5. télécharger le contenu en flux;
6. extraire le texte;
7. supprimer les données temporaires;
8. transmettre le texte au service de découpage.

Le contenu complet ne doit jamais apparaître dans les logs.

### Formats initiaux

| Format | Traitement |
| --- | --- |
| TXT | lire le texte avec un encodage contrôlé |
| DOCX | extraire les paragraphes dans leur ordre |
| PDF texte | extraire le texte page par page |

Un PDF sans texte lisible est marqué `Unsupported` dans la première version.

La taille maximale d’un fichier vient de la configuration. Un fichier trop
grand est ignoré avec une raison explicite.

<a id="m365-sharepoint-archives"></a>
## Traitement des archives

La première version traite ZIP et GZ. D’autres formats peuvent être ajoutés
derrière la même abstraction.

Avant d’extraire une archive, vérifier :

- la taille du fichier compressé;
- la taille totale annoncée après décompression;
- le nombre de fichiers;
- la profondeur des archives imbriquées;
- les chemins des fichiers;
- la présence d’un mot de passe;
- les extensions audio et vidéo à exclure.

Le worker doit bloquer :

- les chemins qui sortent du dossier temporaire;
- les archives récursives sans limite;
- les archives dont la taille décompressée dépasse la limite;
- les archives protégées par mot de passe;
- les archives corrompues.

Chaque fichier contenu produit ses propres passages, mais conserve :

- l’identifiant du document SharePoint parent;
- le nom de l’archive;
- son chemin à l’intérieur de l’archive;
- l’URL SharePoint de l’archive.

Les fichiers contenus héritent des permissions de l’archive. Ils ne possèdent
jamais une ACL plus large que leur document parent.

<a id="m365-sharepoint-pages-lists"></a>
## Traitement des pages et listes

### Pages SharePoint

Pour chaque page moderne :

1. lire son identifiant, titre, URL et date de modification;
2. lire les composants de contenu;
3. extraire uniquement le texte visible et utile;
4. ignorer les scripts et informations de mise en page;
5. découper le texte en passages;
6. conserver les permissions de la page;
7. supprimer les passages lorsque la page est supprimée.

### Listes SharePoint

Chaque élément de liste est un document logique distinct.

Le traitement d’un élément commence uniquement si sa liste est `Enabled` et
`isIndexed = true`. Il reçoit du synchroniseur `siteId`, `listId`,
`listItemId`, `eTag`, `webUrl`, les dates et le dictionnaire `fields`.

#### 1. Charger et filtrer le schéma

Le worker charge le schéma avec :

```http
GET https://graph.microsoft.com/v1.0/sites/{siteId}/lists/{listId}/columns
```

Il suit `@odata.nextLink`, puis conserve le nom interne, le nom affiché et le
type de chaque colonne. Une colonne est exclue du texte lorsque :

- elle est masquée;
- elle appartient à la liste configurable des colonnes interdites;
- elle contient uniquement une donnée technique déjà conservée comme
  métadonnée;
- son type n’est pas supporté et aucune représentation sûre n’existe.

Une colonne inconnue est ignorée avec un diagnostic contenant seulement son
nom et son type. Sa valeur ne doit pas apparaître dans les logs.

#### 2. Convertir les valeurs

| Type SharePoint | Texte produit | Exemple |
| --- | --- | --- |
| texte | valeur normalisée | `Description : Écran 27 pouces` |
| nombre ou devise | nombre avec le nom de colonne | `Montant : 1250 CAD` |
| date | date ISO 8601 en UTC | `Échéance : 2026-09-01T14:00:00Z` |
| booléen | `Oui` ou `Non` | `Urgent : Oui` |
| choix | libellés séparés par une virgule | `Statut : Approuvé, Commandé` |
| personne | nom affiché résolu | `Demandeur : Alice Tremblay` |
| lien | libellé puis URL | `Bon de commande : BC-1042 — https://...` |
| lookup | valeur affichée, jamais l’identifiant seul | `Département : Finance` |

Un identifiant de personne ou de lookup n’est jamais présenté comme s’il était
un identifiant Entra. Lorsque la valeur lisible ne peut pas être résolue, la
colonne est omise et le traitement continue.

#### 3. Construire le document logique

Exemple de valeurs reçues :

```json
{
  "Title": "Ordinateur portable",
  "Status": "Approved",
  "Amount": 1250,
  "Urgent": true
}
```

Exemple de texte envoyé au découpage :

```text
Liste : Demandes d’achat
Élément : Ordinateur portable
Statut : Approuvé
Montant : 1250 CAD
Urgent : Oui
URL : https://contoso.sharepoint.com/sites/finance/Lists/Achats/42
```

Le titre utilise la colonne `Title` lorsqu’elle contient une valeur. Sinon, il
devient `Élément {listItemId} de {nom de la liste}`.

Le document logique transmis au pipeline contient au minimum :

```json
{
  "organizationId": "organisation-interne",
  "contentType": "listItem",
  "siteId": "site-123",
  "listId": "list-456",
  "listItemId": "42",
  "documentVersion": "etag-7",
  "title": "Ordinateur portable",
  "url": "https://contoso.sharepoint.com/sites/finance/Lists/Achats/42",
  "modifiedAt": "2026-08-17T18:30:00Z",
  "content": "Liste : Demandes d’achat\nÉlément : Ordinateur portable\n..."
}
```

#### 4. Vérifier les permissions avant l’indexation

Le worker appelle le résolveur de permissions livré par la fonctionnalité de
sécurité Microsoft 365. Il ne lit pas lui-même les membres ou les groupes.

Le résultat doit être soit une ACL normalisée, soit `Unresolved`. Dans le
second cas, aucun passage, titre ou embedding de l’élément n’est envoyé à Azure
AI Search. Une ACL vide ne signifie jamais « accessible à tous ».

L’API Microsoft Graph v1.0 est utilisée pour les listes, colonnes, valeurs,
deltas et notifications. Les endpoints Graph `/beta` de permissions des
éléments de liste ne sont pas utilisés en production. Le résolveur de
permissions utilise l’API REST SharePoint derrière un client situé dans
`AssistantCore.ExternalServices` :

```http
GET {siteUrl}/_api/web/lists(guid'{listId}')/items({itemId})
    ?$select=HasUniqueRoleAssignments
```

Si l’élément possède des permissions uniques, le client charge ses
`RoleAssignments` et leurs `RoleDefinitionBindings`. Sinon, il remonte vers les
permissions de la liste puis du site. Une erreur, un principal inconnu ou une
réponse partielle produit `Unresolved`.

#### 5. Mettre à jour ou supprimer

- création : produire les passages et utiliser `mergeOrUpload`;
- modification : comparer le `eTag`, reconstruire les passages et supprimer
  ceux qui n’existent plus;
- suppression : supprimer tous les passages correspondant à
  `organizationId + siteId + listId + listItemId`;
- changement de schéma : recalculer l’empreinte des colonnes et reprogrammer
  les éléments affectés;
- changement de permissions : mettre à jour les ACL avant de rendre le nouveau
  contenu recherchable.

#### 6. Limites initiales

Les limites sont configurables et validées au démarrage :

- 100 colonnes indexables par liste;
- 20 000 caractères par valeur;
- 100 000 caractères produits par élément avant découpage;
- 5 000 éléments traités par exécution du worker;
- durée maximale et `CancellationToken` appliqués à chaque appel externe.

Un dépassement n’arrête pas toute la liste. L’élément reçoit un état explicite
comme `ContentTooLarge` ou `TooManyColumns`, sans enregistrer son contenu dans
les logs.

<a id="m365-sharepoint-onedrive"></a>
## Traitement de OneDrive

Dans cette documentation, « OneDrive personnel » désigne le OneDrive
professionnel individuel d’un utilisateur du tenant Microsoft 365.

L’administrateur choisit les OneDrive qui peuvent être indexés. Le fait qu’un
utilisateur appartienne au tenant ne suffit pas pour indexer automatiquement
son contenu.

Chaque OneDrive autorisé possède :

- l’identifiant de son propriétaire;
- son `driveId`;
- son état d’activation;
- son `deltaLink`;
- sa souscription;
- sa dernière synchronisation.

Le même pipeline de fichiers, archives, OCR, chunks, embeddings et permissions
est utilisé pour SharePoint et OneDrive.

Le OneDrive grand public d’un compte Outlook.com ou Hotmail est hors périmètre.
Microsoft exige une connexion déléguée propre à chaque utilisateur et ne
supporte pas les souscriptions applicatives en arrière-plan pour ce scénario.

<a id="m365-sharepoint-chunking"></a>
## Découpage en passages

Un document ne doit pas être envoyé en un seul bloc.

Configuration initiale proposée :

- environ 1 200 tokens par passage;
- chevauchement d’environ 100 tokens;
- ne pas couper au milieu d’un paragraphe lorsque cela est évitable;
- conserver le titre du document;
- conserver le numéro de page lorsqu’il est connu;
- ignorer les passages vides;
- refuser explicitement un document qui dépasse la limite totale de passages,
  sans en indexer seulement une partie.

Ces valeurs restent configurables.

Chaque passage possède une clé déterministe construite à partir de :

```text
organisation + site + bibliothèque + document + numéro du passage
```

<a id="m365-sharepoint-embeddings"></a>
## Création des embeddings

Le fournisseur d’embeddings est accessible par une interface applicative.

Le worker lui transmet seulement :

- le titre utile;
- le texte du passage.

En certification, les embeddings sont produits par le déploiement Azure OpenAI
`m365-text-embedding-3-small` dans `canadacentral`. La configuration indique :

- le fournisseur;
- le modèle;
- le nombre de dimensions;
- la taille maximale d’une demande;
- le nombre maximal de tentatives;
- le délai maximal d’attente.

Le nombre de dimensions du champ Azure AI Search doit correspondre exactement
au modèle utilisé.

Le profil `m365-vector-profile` référence le vectorizer
`m365-azure-openai-vectorizer`. Ce vectorizer transforme les requêtes en
vecteurs avec exactement le même endpoint, le même déploiement et le même
modèle que le worker d’indexation. Les clés restent dans Azure Key Vault et ne
sont pas stockées dans la définition Bicep ou les fichiers de configuration.

Changer de modèle ou de dimensions demande la création d’un nouvel index ou
une réindexation complète contrôlée.

<a id="m365-sharepoint-search-index"></a>
## Structure de l’index Azure AI Search

La première version utilise un index partagé par environnement. Chaque passage
porte obligatoirement l’identifiant interne de son organisation.

Champs proposés :

| Champ | Utilité |
| --- | --- |
| `chunkId` | clé unique du passage |
| `organizationId` | isolation de l’organisation |
| `sourceType` | valeur `sharepoint` |
| `contentType` | fichier, page, élément de liste ou archive |
| `siteId` | site SharePoint source |
| `driveId` | bibliothèque source |
| `documentId` | identifiant Microsoft du fichier |
| `listId` | liste SharePoint lorsque nécessaire |
| `listItemId` | élément de liste lorsque nécessaire |
| `oneDriveOwnerId` | propriétaire du OneDrive lorsque nécessaire |
| `archivePath` | chemin du fichier dans une archive |
| `documentVersion` | version ou `eTag` |
| `chunkNumber` | position du passage |
| `title` | titre du document |
| `content` | texte du passage |
| `url` | lien SharePoint |
| `fileType` | type du fichier |
| `pageNumber` | page lorsque disponible |
| `modifiedAt` | date de modification SharePoint |
| `indexedAt` | date d’indexation |
| `allowedUserIds` | utilisateurs Entra autorisés |
| `allowedGroupIds` | groupes Entra autorisés |
| `allowedSharePointGroupIds` | groupes SharePoint autorisés |
| `hasAnonymousLink` | présence d’un lien anonyme |
| `hasOrganizationLink` | présence d’un lien réservé aux personnes de l’organisation qui possèdent le lien |
| `aclFingerprint` | empreinte canonique des permissions |
| `isAvailable` | passage entièrement publié et consultable |
| `contentVector` | embedding du passage |

`title` et `content` sont recherchables.

`organizationId`, `sourceType`, `siteId`, `driveId`, `documentId`,
`allowedUserIds`, `allowedGroupIds`, `allowedSharePointGroupIds`,
`hasAnonymousLink`, `hasOrganizationLink` et `isAvailable` sont filtrables.

`organizationId`, `allowedUserIds`, `allowedGroupIds`,
`allowedSharePointGroupIds`, `hasAnonymousLink` et `hasOrganizationLink` ne
sont pas récupérables dans Azure AI Search et ne sont donc pas retournés dans
les résultats normaux.
Chaque recherche impose également `isAvailable = true`.

`contentVector` utilise un profil vectoriel compatible avec le modèle
d’embeddings.

<a id="m365-sharepoint-index-updates"></a>
## Mise à jour et suppression

### Nouveau document

Tous les passages sont ajoutés avec `mergeOrUpload`.

### Document modifié

1. traiter la nouvelle version;
2. remplacer les passages qui gardent la même clé;
3. supprimer les anciens passages devenus inutiles;
4. enregistrer le nouveau nombre de passages.

### Document supprimé

Supprimer tous ses `chunkId` de l’index et marquer le document supprimé dans la
base.

### Changement de permissions

L’empreinte de l’ACL est persistée avec l’état du contenu indexé. Lorsque
l’empreinte calculée est identique à l’empreinte persistée, les passages ne
sont pas réécrits.

Lorsqu’elle change, tous les passages du contenu sont d’abord rendus
indisponibles. Leurs champs de permissions sont ensuite mis à jour. La nouvelle
empreinte est persistée et les passages redeviennent disponibles uniquement
après la réussite complète de l’opération. Un échec partiel laisse le contenu
indisponible afin qu’aucune ancienne ACL ne puisse autoriser une recherche.

Une révocation d’accès doit être traitée en priorité.

<a id="m365-sharepoint-document-security"></a>
## Respect des permissions

### Normalisation des identités

Les clés de sécurité sont construites uniquement à partir d’identifiants
stables fournis par Microsoft :

- pour un utilisateur Microsoft Entra, conserver son Object ID `oid`;
- pour un groupe Microsoft Entra, conserver l’Object ID du groupe;
- pour un groupe SharePoint, produire
  `spg:<siteId>:<sharePointGroupId>`.

Le nom affiché et l’adresse courriel sont des données de profil. Ils ne
doivent jamais servir de clé de sécurité.

Les comportements spécialisés des invités, des liens anonymes et des groupes
SharePoint sont complétés par leurs fonctionnalités dédiées sans modifier ce
contrat de normalisation.

### Évaluation des rôles

Seuls les principaux possédant au moins une permission permettant de lire le
contenu sont conservés dans l’ACL.

Le rôle SharePoint `Limited Access`, représenté par `RoleTypeKind = 1`, ne
donne pas accès au contenu lorsqu’il est présent seul.

Un rôle vide, personnalisé ou inconnu qui ne peut pas être évalué de manière
fiable produit `Unresolved`. Le contenu reste alors invisible.

Les identifiants sont dédupliqués et triés avant le calcul de l’empreinte afin
qu’une ACL équivalente produise toujours la même valeur.

### Adapter de résolution des permissions

L’adapter Infrastructure choisit la source de permissions selon le contenu :

- pour un fichier, il lit les permissions avec Microsoft Graph v1.0;
- pour un élément de liste, il utilise l’API REST SharePoint et l’URL exacte
  du site.

L’adapter transforme les réponses externes en `Microsoft365Acl`. Pour un
utilisateur ou un groupe Entra provenant de SharePoint REST, il utilise
uniquement `AadObjectId`.

Chaque autorisation additive est évaluée séparément. Une autorisation connue
et représentable reste utilisable même lorsqu’une autre autorisation du même
document utilise un type de partage plus restrictif ou encore inconnu. Une
autorisation inconnue est ignorée comme source d’accès et n’élargit jamais
l’ACL. Si aucune autorisation fiable ne peut être représentée, le résultat est
`Unresolved` et le document reste invisible.

Pour les liens destinés à des personnes précises (`scope=users`), seuls les
utilisateurs ou groupes possédant un identifiant Microsoft stable sont ajoutés
à l’ACL. Une adresse courriel ou un nom affiché ne suffit jamais.

Les appels Graph utilisent un token Graph. Les appels REST SharePoint utilisent
un token limité à l’origine du tenant SharePoint concerné. Les clients HTTP
n’utilisent pas de cookies et masquent les en-têtes `Authorization`, `Cookie`
et `Set-Cookie` dans les logs. Les tokens, URL de contenu, réponses détaillées
et exceptions externes complètes ne sont jamais journalisés.

Chaque recherche combine obligatoirement :

1. l’organisation interne;
2. l’identifiant Entra de l’utilisateur;
3. les groupes Entra auxquels il appartient.

Exemple conceptuel :

```text
organizationId correspond à l’organisation courante
ET
(
  allowedUserIds contient l’utilisateur
  OU
  allowedGroupIds contient un de ses groupes
)
```

Ces filtres sont construits par le backend. Ils ne sont jamais fournis par le
frontend ou par le modèle d’intelligence artificielle.

Si les permissions du document sont absentes ou incompréhensibles, le document
est invisible.

Si les groupes du membre ne peuvent pas être obtenus, la recherche Microsoft
365 échoue de manière contrôlée. Elle ne retire jamais le filtre.

Une tâche planifiée vérifie périodiquement les permissions afin de couvrir les
changements hérités qui pourraient ne pas produire une notification fiable.

<a id="m365-sharepoint-local-groups"></a>
### Groupes SharePoint locaux

Cette capacité permet à AssistantCore de retrouver un contenu dont l’accès
passe par un groupe SharePoint local, après avoir confirmé que le membre
authentifié appartient encore à ce groupe au moment de la recherche.

Un groupe SharePoint local est créé dans un site SharePoint précis. Les groupes
par défaut `Membres`, `Visiteurs` et `Propriétaires` du site sont des exemples
courants. Un administrateur peut aussi créer un groupe personnalisé. Ces
groupes peuvent contenir des utilisateurs sans posséder d’identifiant de groupe
Microsoft Entra exploitable par Microsoft Graph.

Exemple :

```text
Site Opérations
  -> groupe SharePoint local « Lecteurs des procédures »
       -> Alice
  -> document « Procédure de fermeture.docx »
       -> lecture accordée au groupe « Lecteurs des procédures »
```

Alice peut ouvrir le document dans SharePoint. Pour reproduire correctement ce
droit dans AssistantCore, le système doit conserver le groupe autorisé pendant
l’ingestion, puis vérifier au moment de la recherche qu’Alice appartient encore
à ce groupe.

#### État avant et après l’opération

Avant la prise en charge de cette capacité, AssistantCore peut reconnaître le
groupe dans les permissions du document, mais ne peut pas confirmer
l’appartenance du membre au moment de la recherche. Le comportement sécurisé
consiste donc à ne pas retourner le document.

Après son implémentation, AssistantCore peut retrouver les groupes SharePoint
locaux du membre pour chaque site autorisé et inclure ces groupes dans le filtre
de sécurité Azure AI Search. Cette opération ne change pas le score textuel, le
score vectoriel ou le reranking. Elle décide seulement quels passages peuvent
être présentés au moteur de recherche et au modèle.

#### Identifiant stable

Un groupe SharePoint reçoit un identifiant stable composé du site et de
l’identifiant du groupe :

```text
spg:<siteId>:<sharePointGroupId>
```

L’identifiant du site est obligatoire, car deux sites différents peuvent
utiliser le même identifiant numérique de groupe. Le système conserve ces
identifiants dans `allowedSharePointGroupIds` pour chaque passage concerné.

#### Flow détaillé d’ingestion

1. Le Worker synchronise un fichier ou un élément de liste appartenant à une
   source explicitement autorisée par l’organisation.
2. Le flow applicatif passe par le handler et le service de traitement du
   document. L’adapter Infrastructure lit ensuite les permissions avec Graph
   ou avec l’API REST SharePoint selon le type de contenu.
3. Lorsqu’une permission vise un groupe SharePoint local, l’adapter récupère
   l’identifiant numérique du principal et le combine avec l’identifiant du
   site.
4. Le passage envoyé à Azure AI Search reçoit la valeur normalisée
   `spg:<siteId>:<sharePointGroupId>` dans `allowedSharePointGroupIds`.
5. Une permission incomplète, ambiguë ou impossible à rattacher au site rend
   le contenu non résolu. Elle ne doit jamais transformer le contenu en document
   public.

#### Flow détaillé de recherche

1. Un membre authentifié envoie un message. Le backend déduit son organisation,
   son identifiant Entra et son adresse de connexion depuis le contexte
   authentifié; le frontend et le modèle ne fournissent aucun identifiant de
   sécurité.
2. Le flow reste `Controller -> IDispatcher -> CommandHandler -> service
   applicatif`. Lorsque le modèle choisit l’outil Microsoft 365, le connecteur
   Infrastructure reçoit le contexte d’exécution déjà validé.
3. Le connecteur récupère d’abord les groupes Microsoft Entra du membre avec
   Microsoft Graph. Il récupère aussi les groupes Microsoft 365 dont ce membre
   est propriétaire.
4. Pour chaque site SharePoint autorisé dans l’organisation, un adapter
   Infrastructure demande un token limité à l’origine
   `https://<tenant>.sharepoint.com/.default`.
5. L’API REST SharePoint recherche l’utilisateur du site par son adresse de
   connexion et retourne les groupes SharePoint locaux auxquels il appartient.
6. Chaque groupe retourné est normalisé avec l’identifiant du site. Les valeurs
   sont dédupliquées, triées et mises en cache pendant cinq minutes afin de ne
   pas appeler SharePoint pour chaque recherche.
7. Le repository de recherche construit un filtre combinant obligatoirement
   l’organisation, l’utilisateur, ses groupes Entra et ses groupes SharePoint
   locaux.
8. Azure AI Search applique le filtre avant de retourner les candidats. Le
   reranking travaille uniquement sur ces candidats déjà autorisés.

Exemple conceptuel :

```text
organizationId correspond à l’organisation courante
ET
(
  allowedUserIds contient l’utilisateur
  OU allowedGroupIds contient un de ses groupes Entra
  OU allowedSharePointGroupIds contient un de ses groupes SharePoint locaux
)
```

Les claims SharePoint terminés par `_o` représentent uniquement les
propriétaires d’un groupe Microsoft 365. Ils sont indexés sous la forme
`m365go:<groupId>` et comparés aux groupes réellement possédés par le membre.
Ils ne sont jamais remplacés par le simple identifiant Entra du groupe, car ce
remplacement donnerait incorrectement l’accès à tous ses membres.

#### Authentification SharePoint

Microsoft Graph continue d’utiliser le secret client existant. L’API REST
SharePoint utilise une authentification Entra App-Only par certificat. La
configuration attendue comprend le chemin absolu du PFX et son mot de passe.
La partie publique du certificat est téléversée dans l’App Registration et la
permission d’application `SharePoint -> Sites.Read.All` reçoit le consentement
administrateur. La permission Microsoft Graph portant le même nom ne remplace
pas cette permission SharePoint.

Le PFX et sa clé privée restent hors du dépôt. Ils doivent être fournis par un
gestionnaire de secrets ou un volume sécurisé, renouvelés avant expiration et
ne jamais apparaître dans les logs.

#### Échecs et changements d’appartenance

Lors d’une recherche, il détermine les groupes SharePoint auxquels appartient
le membre pour le site concerné. Cette résolution doit être mise en cache
pendant une courte durée, mais une panne du cache ou de SharePoint ferme
l’accès.

Les modifications de membres d’un groupe SharePoint doivent être détectées par
une réconciliation planifiée et testées séparément.

Sans certificat configuré, les contenus accessibles uniquement par un groupe
SharePoint local restent invisibles, sans retirer les protections existantes
pour les utilisateurs et groupes Entra. Lorsqu’un certificat est configuré,
un token refusé, un certificat expiré, une permission SharePoint manquante, une
réponse partielle ou une indisponibilité de SharePoint fait échouer la
résolution. Le backend ne retire jamais le filtre pour maintenir
artificiellement la disponibilité.

### Utilisateurs invités externes

Un invité doit avoir un objet utilisateur invité dans le tenant Microsoft
Entra du client. L’identifiant utilisé est l’`oid` de cet objet invité dans le
tenant du client.

Pour utiliser AssistantCore, l’invité doit aussi :

- être affecté explicitement à `AssistantCore.Access`;
- appartenir à l’organisation interne correspondante;
- posséder un membre AssistantCore actif;
- réussir les mêmes contrôles que les utilisateurs internes.

Une adresse courriel externe seule ne constitue jamais une autorisation.

Une invitation SharePoint non encore acceptée ne donne pas accès au document
dans AssistantCore.

### Liens « Personnes de l’organisation disposant du lien »

Un lien de portée `organization` est fondé sur deux conditions : la personne
appartient au tenant et elle possède le lien. Il ne doit donc pas être converti
en droit de découverte accordé à tous les membres de l’organisation.

Le document est indexé avec `hasOrganizationLink=true`. Ce champ décrit le
type de partage et participe à l’empreinte des permissions, mais il n’accorde
pas à lui seul l’accès dans une recherche normale. Les autorisations
utilisateur et groupe présentes sur le même document continuent d’être
appliquées normalement. Une future recherche initiée depuis le lien exact doit
valider ce lien auprès de Microsoft avant d’utiliser ce droit.

### Liens « Toute personne disposant du lien »

Un lien anonyme est une autorisation fondée sur la possession du lien. Il ne
doit pas être transformé en permission générale de découverte.

Le document est indexé avec `hasAnonymousLink=true`, mais ce champ ne permet
pas à lui seul de le retourner dans une recherche normale.

Le document devient accessible dans AssistantCore seulement si :

- le membre possède aussi une permission utilisateur ou groupe normale; ou
- il fournit le lien de partage exact et le backend le valide auprès de
  Microsoft avant de retourner le contenu.

Le lien complet, qui peut contenir un secret d’accès, ne doit jamais être
stocké dans les logs, retourné au modèle ou exposé comme métadonnée de
recherche.

<a id="m365-sharepoint-search-connector"></a>
## Connecteur de recherche Microsoft 365

> Portee du chantier RAG agentique : les ameliorations de recherche decrites
> dans cette section s'appliquent aux contenus deja extractibles. L'ajout de
> nouveaux formats documentaires appartient a un ticket distinct.

Le registre contient déjà la définition logique de
`search_microsoft_365`, mais son exécution complète doit être ajoutée.

Le connecteur :

1. reçoit une question de recherche validée;
2. obtient l’organisation et l’utilisateur courants;
3. obtient les groupes autorisés;
4. crée l’embedding de la question;
5. decompose une question complexe en sous-requetes bornees lorsque cela
   ameliore la couverture;
6. effectue une recherche hybride textuelle et vectorielle;
7. ajoute les filtres d’organisation, de permissions, de source et de date;
8. classe semantiquement un ensemble de candidats suffisamment large;
9. conserve les meilleurs passages et leur contexte de document ou de section;
10. normalise les preuves;
11. retourne le titre, le passage, l’URL et la provenance;
12. ne retourne jamais les ACL au modèle.

La résolution des groupes SharePoint locaux ne fait pas partie du périmètre
actuel. Elle sera ajoutée dans le chantier décrit à la section
[Groupes SharePoint locaux](#m365-sharepoint-local-groups). Le connecteur actuel
limite donc son contexte de recherche à l’utilisateur et aux groupes Microsoft
Entra.

Le nombre de candidats, le nombre final de passages, la configuration du
classement semantique et les seuils de pertinence sont configurables. Ils sont
calibres avec le jeu d'evaluation du produit et non avec une seule question.

Le passage indexe conserve un contexte court qui permet d'identifier le
document, la section, l'entite et la periode lorsque ces informations sont
definies hors du fragment. Le contenu original reste disponible pour la
citation et n'est pas remplace par ce contexte genere.

Une recherche sans résultat retourne une collection vide. Une panne du
connecteur retourne un échec contrôlé pouvant devenir un avertissement dans la
réponse de l’assistant.

<a id="m365-sharepoint-errors"></a>
## Gestion des erreurs

### Erreurs temporaires

Réessayer avec une attente progressive :

- limitation Microsoft Graph `429`;
- erreur Microsoft `5xx`;
- délai d’attente réseau;
- erreur temporaire d’embedding;
- erreur temporaire Azure AI Search;
- indisponibilité Azure Service Bus.

Une ACL momentanément impossible à résoudre reste également récupérable. Le
Worker effectue les premières reprises avec le délai normal des documents. Une
fois le nombre habituel de tentatives atteint, il conserve le travail en échec
temporaire et utilise l’intervalle long de réconciliation ACL. Une correction
de permission ou la prise en charge d’un nouveau type de partage ne laisse donc
pas définitivement le document hors de l’index.

Respecter `Retry-After` lorsqu’il est fourni.

### Erreurs permanentes

Ne pas réessayer indéfiniment :

- format non supporté;
- document chiffré;
- site retiré;
- consentement supprimé;
- configuration invalide.

### Informations de suivi

Les logs peuvent contenir :

- organisation interne;
- site, bibliothèque, liste et contenu techniques;
- étape du traitement;
- durée;
- code d’erreur;
- numéro de tentative.

Ils ne contiennent jamais :

- token Microsoft;
- secret;
- `clientState`;
- texte complet du document;
- embedding complet;
- contenu de la question utilisateur.

<a id="m365-sharepoint-configuration"></a>
## Configuration et secrets

Configuration non secrète attendue :

```json
{
  "Microsoft365": {
    "ClientId": "<application-multitenant>",
    "AuthorityBaseUrl": "https://login.microsoftonline.com",
    "GraphBaseUrl": "https://graph.microsoft.com",
    "SharePointCertificatePath": "",
    "SharePointCertificateBase64": "",
    "SharePointGroupCacheMinutes": 5,
    "ConsentCallbackUrl": "https://<api>/api/microsoft365/consent/callback",
    "ConsentSuccessRedirectUrl": "https://<frontend>/microsoft365/consent/success",
    "ConsentErrorRedirectUrl": "https://<frontend>/microsoft365/consent/error",
    "ConsentStateLifetimeMinutes": 10,
    "WebhookBaseUrl": "https://<url-publique>",
    "SynchronizationLeaseMinutes": 15,
    "SynchronizationIntervalMinutes": 15,
    "PermissionReconciliationIntervalHours": 6,
    "MaximumFileSizeBytes": 20971520,
    "MaximumListColumns": 100,
    "MaximumListFieldCharacters": 20000,
    "MaximumListItemCharacters": 100000,
    "MaximumListItemsPerRun": 5000
  },
  "ServiceBus": {
    "Enabled": false,
    "FullyQualifiedNamespace": "<namespace>.servicebus.windows.net",
    "DriveSyncQueue": "sharepoint-drive-sync",
    "DocumentProcessQueue": "sharepoint-document-process",
    "ListSyncQueue": "sharepoint-list-sync",
    "ListItemProcessQueue": "sharepoint-list-item-process"
  },
  "AzureSearch": {
    "Endpoint": "https://<service>.search.windows.net",
    "IndexName": "microsoft-content-dev"
  },
  "Embeddings": {
    "Provider": "<provider>",
    "Model": "<model>",
    "Dimensions": 1536,
    "ChunkTokenLimit": 800,
    "ChunkTokenOverlap": 100
  }
}
```

Le démarrage refuse une URL non HTTPS, un `ClientId` vide, un secret absent ou
une durée de `state` hors de la plage de 1 à 60 minutes. Le secret reste absent
des fichiers versionnés et provient de `user-secrets` en local.

`SharePointCertificatePath` et `SharePointCertificateBase64` sont mutuellement
exclusifs. Le chemin est utilisé en local. CERTIF injecte le PFX encodé en
Base64 et son mot de passe depuis Key Vault.

La valeur `Dimensions` est un exemple et doit correspondre au modèle
réellement choisi.

En local, les secrets utilisent `dotnet user-secrets`.

Le secret de l’App Registration Microsoft 365 est configuré sans être ajouté à
`appsettings.json` :

```bash
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:ClientSecret" "<secret>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:SharePointCertificatePath" "<chemin-absolu-du-pfx>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:SharePointCertificatePassword" "<mot-de-passe-du-pfx>"
dotnet user-secrets --project AssistantCore.Service set "AzureSearch:ApiKey" "<clé>"
```

Dans Azure :

- utiliser les identités managées pour Service Bus et Azure AI Search;
- utiliser un certificat ou une identité fédérée pour Microsoft Graph;
- utiliser Key Vault seulement lorsqu’un secret reste nécessaire;
- ne jamais placer un secret dans Git ou dans `appsettings.json`.

<a id="m365-sharepoint-local-development"></a>
## Développement local

Le développeur a besoin :

- du tenant Microsoft 365 fictif;
- d’un site SharePoint de test;
- du service Azure AI Search de développement;
- du fournisseur d’embeddings configuré;
- de ngrok ou d’un tunnel HTTPS équivalent.

En mode local, Service Bus reste désactivé : les synchronisations persistées
dans SQL sont réclamées directement par le Worker. Le transport Service Bus est
réservé à un environnement où les files et leurs consommateurs sont déployés.

Ordre de démarrage manuel :

1. démarrer SQL Server;
2. démarrer AssistantCore.Service;
3. démarrer l’hôte de webhooks;
4. démarrer le worker;
5. ouvrir le tunnel HTTPS vers le port du webhook;
6. configurer l’URL publique;
7. créer ou recréer les souscriptions;
8. vérifier la validation Microsoft;
9. ajouter un fichier de test.

Une URL de tunnel modifiée demande de mettre à jour ou recréer les
souscriptions concernées.

Le script `scripts/start-local-live.sh` automatise cet ordre : il démarre
l'API, vérifie que le webhook public retourne correctement un token de
validation, démarre ngrok si nécessaire, puis lance le Worker. Il s'arrête
avant le Worker si le webhook reste inaccessible.

<a id="m365-sharepoint-end-to-end-test"></a>
## Test complet avec le tenant fictif

### Préparation Microsoft 365

Créer dans le tenant fictif :

- un utilisateur `Alice`;
- un utilisateur `Bob`;
- un groupe Entra `EmployesTest` contenant Alice et Bob;
- un groupe Entra `FinanceTest` contenant seulement Alice;
- un site SharePoint `AssistantCore Test`;
- une bibliothèque `Documents`.

Ajouter :

- `guide-general.txt`, accessible à `EmployesTest`;
- `manuel-employe.pdf`, accessible à `EmployesTest`;
- `budget-confidentiel.docx`, accessible seulement à `FinanceTest`.

Ne jamais utiliser de données réelles dans ce test.

### Test 1 — Connexion

1. connecter le tenant fictif;
2. vérifier le consentement;
3. découvrir le site;
4. activer le site;
5. découvrir la bibliothèque;
6. vérifier que la souscription existe.

### Test 2 — Synchronisation initiale

1. lancer la synchronisation;
2. attendre la fin du worker;
3. vérifier que chaque document supporté possède des passages;
4. vérifier que chaque passage possède un vecteur;
5. vérifier que les URL SharePoint sont correctes;
6. relancer la synchronisation;
7. vérifier qu’aucun doublon n’est créé.

### Test 3 — Recherche

1. rechercher un mot exact;
2. rechercher la même idée avec des mots différents;
3. vérifier que la recherche hybride retrouve le bon document;
4. poser une question avec `POST /api/messages`;
5. vérifier que la réponse cite le document.

### Test 4 — Permissions

1. se connecter comme Alice;
2. vérifier qu’Alice trouve le budget;
3. se connecter comme Bob;
4. vérifier que Bob ne trouve jamais le budget;
5. rechercher comme Bob le titre exact du budget;
6. vérifier que le document reste absent des résultats, preuves et sources.

### Test 5 — Nouveau fichier

1. ajouter un fichier dans SharePoint;
2. vérifier que Microsoft appelle le webhook;
3. vérifier qu’un message est placé en file;
4. vérifier que le worker exécute le delta;
5. vérifier que le document devient recherchable.

### Test 6 — Modification

1. modifier le contenu d’un fichier;
2. attendre la synchronisation;
3. vérifier que le nouveau texte est trouvé;
4. vérifier que l’ancien texte a disparu;
5. vérifier qu’aucun ancien passage inutile ne reste.

### Test 7 — Suppression

1. supprimer un fichier;
2. attendre la synchronisation;
3. vérifier que tous ses passages disparaissent;
4. vérifier qu’il ne peut plus être cité.

### Test 8 — Changement de permissions

1. donner à Bob l’accès au budget;
2. attendre la synchronisation des permissions;
3. vérifier que Bob trouve le budget;
4. retirer ensuite son accès;
5. attendre la synchronisation;
6. vérifier que Bob ne trouve plus le budget.

### Test 9 — Webhook indisponible

1. arrêter l’hôte de webhooks;
2. modifier un fichier;
3. redémarrer l’hôte;
4. exécuter la réconciliation planifiée;
5. vérifier que la modification est tout de même indexée.

### Test 10 — Isolation

1. créer une deuxième organisation interne de test;
2. ajouter un passage fictif pour cette organisation;
3. rechercher depuis le tenant Microsoft 365 fictif principal;
4. vérifier que le passage de l’autre organisation n’est jamais retourné.

### Test 11 — Documents Office

1. ajouter un Word, un Excel et un PowerPoint;
2. vérifier que le texte et la structure utile sont extraits;
3. vérifier que chaque fichier est recherchable;
4. vérifier les numéros de feuilles, pages ou diapositives disponibles.

### Test 12 — Autres formats

1. tester TXT, CSV, JSON, XML, HTML, Markdown et OpenDocument;
2. vérifier chaque extracteur;
3. vérifier qu’un fichier binaire sans texte ne bloque pas le worker;
4. vérifier que les fichiers audio et vidéo sont ignorés.

### Test 13 — OCR

1. ajouter une image contenant du texte;
2. ajouter un PDF scanné;
3. vérifier le texte OCR;
4. vérifier la recherche textuelle et vectorielle;
5. vérifier le comportement d’un résultat OCR vide.

### Test 14 — Archives

1. ajouter une archive contenant plusieurs formats;
2. vérifier chaque fichier extrait;
3. tester une archive imbriquée;
4. tester une archive corrompue;
5. tester une archive trop volumineuse;
6. tester un chemin d’extraction dangereux;
7. vérifier l’héritage des permissions.

### Test 15 — Pages SharePoint

1. créer une page moderne;
2. vérifier son indexation;
3. modifier son texte;
4. modifier ses permissions;
5. supprimer la page;
6. vérifier chaque changement dans Azure AI Search.

### Test 16 — Listes SharePoint

1. créer `Demandes d’achat` avec les colonnes `Title` (texte), `Amount`
   (nombre), `DueDate` (date), `Urgent` (booléen), `Status` (choix),
   `Requester` (personne) et `PurchaseOrder` (lien);
2. vérifier que la découverte enregistre la liste comme `Discovered` sans
   indexer ses éléments;
3. appeler l’endpoint d’activation et vérifier la création de la souscription
   et de la première synchronisation;
4. ajouter l’élément `Ordinateur portable`, puis vérifier que son texte contient
   les noms affichés et les valeurs lisibles;
5. ajouter une colonne masquée `InternalApprovalCode` et vérifier que son nom et
   sa valeur sont absents de l’index et des logs;
6. modifier `Amount` et `Status`, exécuter le delta et vérifier que l’ancien
   contenu n’est plus recherchable;
7. donner une permission unique à Alice, puis vérifier qu’Alice trouve
   l’élément et que Bob ne le trouve pas même avec le titre exact;
8. provoquer une erreur du résolveur de permissions et vérifier que l’élément
   devient invisible au lieu d’être rendu public;
9. supprimer l’élément et vérifier que tous ses `chunkId` disparaissent;
10. désactiver la liste et vérifier l’arrêt de la souscription et la suppression
    de tous les passages de cette liste.

### Test 17 — OneDrive professionnel

1. autoriser le OneDrive d’Alice;
2. ajouter, modifier et supprimer un fichier;
3. vérifier les webhooks et le delta;
4. vérifier que le OneDrive non autorisé de Bob n’est pas indexé.

### Test 18 — Invité externe

1. inviter un utilisateur externe dans le tenant fictif;
2. lui attribuer `AssistantCore.Access`;
3. partager un document avec cet invité;
4. vérifier qu’il trouve le document;
5. retirer le partage;
6. vérifier qu’il ne trouve plus le document.

### Test 19 — Lien anonyme

1. créer un lien « Toute personne »;
2. vérifier que le document n’est pas découvrable par son titre;
3. fournir le lien exact;
4. vérifier que le backend valide le lien;
5. révoquer le lien;
6. vérifier que l’accès disparaît.

<a id="m365-sharepoint-local-groups-testing"></a>
### Test 20 — Guide détaillé des groupes SharePoint locaux

Ce scénario valide la résolution des appartenances aux groupes SharePoint
locaux. Il exige un tenant de certification, deux utilisateurs de test, un site
sans données sensibles, un index Azure AI Search dédié et un certificat
App-Only valide.

#### Préparer Entra ID et AssistantCore

1. créer ou sélectionner un certificat de test dont la clé privée est exportée
   dans un PFX protégé par mot de passe;
2. téléverser uniquement la partie publique du certificat dans l’App
   Registration utilisée par AssistantCore;
3. ajouter la permission d’application `SharePoint -> Sites.Read.All`, puis
   accorder le consentement administrateur;
4. configurer le chemin absolu du PFX et son mot de passe dans le magasin de
   secrets utilisé par l’API et le Worker;
5. redémarrer l’API et le Worker afin qu’ils relisent la configuration;
6. vérifier qu’aucun PFX, mot de passe, token ou en-tête `Authorization`
   n’apparaît dans les logs.

#### Préparer les utilisateurs et le contenu

1. créer ou sélectionner deux utilisateurs de certification, Alice et Bob;
2. leur attribuer l’accès à AssistantCore et créer leurs membres internes;
3. créer dans SharePoint un site de test explicitement autorisé dans
   AssistantCore;
4. créer dans ce site un groupe local `AssistantCore - Lecteurs locaux` sans
   créer de groupe Microsoft Entra correspondant;
5. ajouter Alice au groupe et vérifier que Bob n’en fait pas partie;
6. créer un document dont le titre et le contenu contiennent une valeur unique,
   par exemple `SPLOCAL-ALPHA-2026`;
7. retirer les droits hérités du document si nécessaire et accorder la lecture
   uniquement au groupe local de test;
8. lancer la synchronisation et attendre que le document et ses ACL soient
   publiés dans l’index de certification.

#### Vérifier l’indexation

1. inspecter le document dans l’outil d’administration de l’index;
2. vérifier que `organizationId` correspond à l’organisation de test;
3. vérifier que `allowedSharePointGroupIds` contient une valeur au format
   `spg:<siteId>:<groupId>`;
4. vérifier que le champ ne contient ni le nom du groupe, ni une adresse
   courriel, ni une valeur provenant du frontend;
5. vérifier qu’un groupe portant le même identifiant numérique dans un autre
   site produit une valeur différente grâce au `siteId`.

#### Vérifier les recherches autorisées et interdites

1. se connecter comme Alice et rechercher `SPLOCAL-ALPHA-2026`;
2. vérifier que le document apparaît et que sa citation pointe vers la bonne
   URL SharePoint;
3. se connecter comme Bob et lancer exactement la même recherche;
4. vérifier que le document est absent des résultats, preuves, citations et
   réponses du modèle;
5. vérifier dans les traces techniques que le filtre d’organisation reste
   présent et que le reranking n’a reçu aucun passage interdit pour Bob.

#### Vérifier un changement d’appartenance

1. retirer Alice du groupe SharePoint local et ajouter Bob;
2. attendre au moins la durée du cache configurée ou invalider le cache par le
   mécanisme prévu pour l’environnement de test;
3. relancer la réconciliation des permissions si le document ou son ACL a été
   modifié;
4. répéter la recherche comme Alice et vérifier que le document est maintenant
   absent;
5. répéter la recherche comme Bob et vérifier que le document est maintenant
   visible;
6. mesurer et consigner le délai entre la modification SharePoint et son effet
   dans AssistantCore.

#### Vérifier les erreurs de sécurité

1. retirer temporairement le chemin du certificat et vérifier que les contenus
   accessibles uniquement par un groupe SharePoint local restent invisibles;
2. utiliser un certificat expiré ou non enregistré et vérifier un échec
   contrôlé sans donnée sensible dans les logs;
3. retirer le consentement `SharePoint -> Sites.Read.All` et vérifier que le
   `401` ou `403` SharePoint ferme l’accès;
4. simuler une réponse SharePoint invalide ou une interruption réseau et
   vérifier qu’aucun contenu protégé uniquement par un groupe local ne devient
   visible;
5. restaurer le certificat et la permission, puis vérifier le retour au
   fonctionnement normal sans modifier les ACL de l’index.

#### Tests automatisés attendus

- le client d’identité utilise une assertion signée par certificat pour le
  scope SharePoint et n’envoie pas de `client_secret`;
- le client SharePoint encode correctement l’adresse et lit les identifiants
  des groupes;
- le résolveur limite les appels aux sites autorisés, normalise les identifiants
  avec le site, déduplique les résultats et respecte le cache;
- le filtre Azure AI Search combine utilisateur, groupes Entra et groupes
  SharePoint sans accepter de valeurs fournies par le modèle;
- Alice obtient le document et Bob ne l’obtient pas;
- deux groupes numériques identiques provenant de sites différents ne se
  confondent jamais;
- toute erreur de certificat, de permission, de cache ou de réponse externe
  produit un refus fermé;
- les tests d’architecture confirment que les SDK et appels réseau restent dans
  `AssistantCore.ExternalServices` et leurs adapters Infrastructure.

<a id="m365-sharepoint-test-tickets"></a>
## Stratégie des tickets et des tests

Chaque capacité possède un seul ticket d'implémentation. Ce ticket contient
directement :

1. les étapes techniques ordonnées;
2. les tests unitaires, d'intégration et d'architecture propres à la capacité;
3. les cas normaux, limites, erreurs et contrôles de sécurité;
4. les critères d'acceptation nécessaires pour considérer l'implémentation
   terminée.

Une capacité ne doit pas être considérée terminée avant la réussite de ses
tests. Un second ticket qui répète uniquement ces contrôles n'est pas créé.

Un seul ticket final couvre la validation manuelle de bout en bout dans le
tenant Microsoft 365 fictif. Il vérifie les relations entre plusieurs
capacités qui ne peuvent pas être prouvées isolément : consentement, webhook,
delta, extraction, permissions, isolation, recherche et réponse citée de
`/api/messages`.

Cette validation finale ne remplace pas les tests de chaque ticket
d'implémentation et ne sert pas à reporter leur définition de terminé.

<a id="m365-sharepoint-implementation-order"></a>
## Ordre d’implémentation

Implémenter dans cet ordre :

1. modèle de persistence Microsoft 365;
2. clients externes Microsoft Graph;
3. connexion et découverte des sites;
4. Azure Service Bus;
5. hôte public de webhooks;
6. gestion des souscriptions;
7. worker et synchronisation delta;
8. téléchargement et extraction;
9. découpage en passages;
10. fournisseur d’embeddings;
11. écriture Azure AI Search;
12. synchronisation des permissions;
13. connecteur `search_microsoft_365`;
14. intégration avec `/api/messages`;
15. tests automatisés;
16. test manuel complet dans le tenant fictif.

Chaque étape doit être utilisable ou testable avant de commencer la suivante.

<a id="m365-sharepoint-definition-of-done"></a>
## Définition de terminé

La fonctionnalité est terminée seulement si :

- le tenant fictif peut être connecté;
- le site pilote peut être activé;
- les souscriptions sont créées et renouvelées;
- le worker fonctionne séparément de l’API;
- les formats annoncés sont traités;
- les passages et embeddings sont présents dans Azure AI Search;
- une relance ne crée pas de doublon;
- créations, modifications et suppressions sont synchronisées;
- les changements de permissions sont appliqués;
- Bob ne peut jamais retrouver le document réservé à Alice;
- les documents d’une autre organisation sont invisibles;
- `/api/messages` peut utiliser `search_microsoft_365`;
- la réponse cite l’URL SharePoint;
- aucun secret ou contenu complet n’apparaît dans les logs;
- les erreurs temporaires sont réessayées;
- les erreurs permanentes sont visibles et compréhensibles;
- tous les nouveaux tests respectent les conventions du projet;
- les tests d’architecture réussissent;
- `dotnet test Solution.sln` réussit.

<a id="m365-sharepoint-references"></a>
## Références

- [Consentement administrateur Microsoft Identity](https://learn.microsoft.com/en-us/entra/identity-platform/v2-admin-consent)
- [Flow OAuth 2.0 avec informations d'identification du client](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-client-creds-grant-flow)
- [Lire l'organisation avec Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/organization-list)
- [Lister les sites avec Microsoft Graph v1.0](https://learn.microsoft.com/en-us/graph/api/site-list?view=graph-rest-1.0)
- [Notifications Microsoft Graph](https://learn.microsoft.com/en-us/graph/change-notifications-overview)
- [Recevoir les notifications par webhook](https://learn.microsoft.com/en-us/graph/change-notifications-delivery-webhooks)
- [Créer une souscription](https://learn.microsoft.com/en-us/graph/api/subscription-post-subscriptions)
- [Renouveler une souscription](https://learn.microsoft.com/en-us/graph/api/subscription-update)
- [Suivre les changements d’une bibliothèque](https://learn.microsoft.com/en-us/graph/api/driveitem-delta)
- [Lister les listes d’un site](https://learn.microsoft.com/en-us/graph/api/list-list)
- [Lister les colonnes d’une liste](https://learn.microsoft.com/en-us/graph/api/list-list-columns)
- [Suivre les changements des éléments d’une liste](https://learn.microsoft.com/en-us/graph/api/listitem-delta)
- [API REST SharePoint](https://learn.microsoft.com/en-us/sharepoint/dev/sp-add-ins/get-to-know-the-sharepoint-rest-service)
- [Permissions d’un élément de liste — Graph beta, non utilisé en production](https://learn.microsoft.com/en-us/graph/api/listitem-get-permissions?view=graph-rest-beta)
- [Lire les permissions d’un fichier](https://learn.microsoft.com/en-us/graph/api/driveitem-list-permissions)
- [Créer un index vectoriel](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-create-index)
- [Exécuter une recherche vectorielle](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-query)
- [Exécuter une recherche hybride](https://learn.microsoft.com/en-us/azure/search/hybrid-search-how-to-query)
- [Émulateur Azure Service Bus](https://learn.microsoft.com/azure/service-bus-messaging/overview-emulator)


<a id="vector-metric-migration"></a>
## Métrique vectorielle et migration

`Rag:VectorSearch:Metric` vaut explicitement `cosine`, cohérent avec la stratégie
actuelle `text-embedding-3-small`. Une autre métrique est rejetée. Azure recommande
cosine pour les embeddings Azure OpenAI :
[création d'un index vectoriel](https://learn.microsoft.com/en-us/azure/search/vector-search-how-to-create-index).
Le score Semantic Ranker (0 à 4) reste distinct du score de recherche hybride :
[fonctionnement du Semantic Ranker](https://learn.microsoft.com/en-us/azure/search/semantic-search-overview).

Le service et le Worker doivent recevoir la même configuration Rag et AzureSearch.
L'initialisation écrit `hnswParameters.metric` dans le profil `m365-hnsw`. Si l'index
existant déclare une métrique différente, elle échoue avant toute écriture avec une
instruction de migration. Aucun index n'est supprimé automatiquement. Une définition
historique sans métrique explicite reçoit désormais cosine ; si Azure refuse cette
mise à jour, utiliser la migration ci-dessous plutôt que supprimer l'index actif.

Pour migrer une métrique incompatible : créer un nouvel index sous un autre nom,
configurer un Worker pour alimenter ce nouvel index, puis effectuer une réingestion
complète des sources et ACL (ne pas se limiter aux deltas ou aux versions déjà
marquées comme indexées). Vérifier la complétude, les permissions et les résultats
avant de basculer `AzureSearch:IndexName` pour les lecteurs et les writers. Conserver
l'ancien index pour revenir à l'ancienne configuration en cas de problème. Ne le
supprimer qu'après validation opérationnelle. La migration et le basculement Azure
restent des opérations explicites de déploiement ; le démarrage ne les effectue pas.

Voir [Évaluation automatisée de l’agent et de la recherche](../../../recherche/rag-agentique/evaluation-automatisee.md#flow).
### PDF et images

Les PDF sont d'abord lus avec leur couche de texte native. L'OCR Azure AI Vision Read est utilise uniquement lorsqu'une page est vide ou contient moins de texte que le seuil configure. Pour un PDF mixte, les pages lisibles restent natives et seules les pages insuffisantes sont remplacees par leur resultat OCR.

Les images sont envoyees a Azure AI Vision Read uniquement pour les formats autorises. Un resultat sans texte retourne `NoIndexableContent`; un timeout ou une indisponibilite du fournisseur conserve un statut technique explicite et ne doit pas etre transforme en contenu vide indexable. Les images et textes complets ne sont jamais journalises.
