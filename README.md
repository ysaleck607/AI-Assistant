# AssistantCore

Backend du service AssistantCore.

Le projet fournit actuellement :

- une API ASP.NET Core;
- une authentification Microsoft Entra ID multitenant;
- une separation des donnees par organisation;
- une base SQL Server initialisee avec Flyway;
- une collection Postman pour tester les endpoints authentifies.

## Prerequis

- .NET SDK 10;
- Docker Desktop avec Docker Compose;
- Postman pour les tests OAuth 2.0;
- un acces a Microsoft Entra ID pour tester l'authentification.

Verifier les installations :

```bash
dotnet --version
docker --version
docker compose version
```

Pour configurer CERTIF dans Azure Container Apps, suivre le guide
[Installer CERTIF dans Azure Container Apps](docs/operations/azure-dev-certif-setup.md).

[Activer un nouveau client](docs/operations/client-onboarding.md).

## Demarrage rapide

### 1. Restaurer et compiler

```bash
dotnet restore Solution.sln
dotnet build Solution.sln
```

### 2. Configurer la base locale

Creer un fichier `.env.database` a la racine :

```dotenv
SQL_SERVER_PASSWORD=Password123!
SQLPAD_ADMIN_EMAIL=admin@local.dev
SQLPAD_ADMIN_PASSWORD=Password123!
```

Ces identifiants servent uniquement au developpement local. Le fichier est
ignore par Git et ne doit pas etre utilise en production.

### 3. Demarrer SQL Server

```bash
bash scripts/start-database-stack.sh
```

Ce script demarre :

- SQL Server sur `localhost:1433`;
- Flyway, qui cree la base et applique les migrations;
- SQLPad sur `http://localhost:3000`.

Verifier les conteneurs :

```bash
docker compose --env-file .env.database ps
```

### 4. Demarrer l'API

Si necessaire, approuver le certificat HTTPS local :

```bash
dotnet dev-certs https --trust
```

Demarrer le service :

```bash
dotnet run --project AssistantCore.Service --launch-profile https
```

Swagger est disponible sur :

```text
https://localhost:7292/swagger
```

## Deux modes de démarrage local

### Mode local sans vrais secrets

Ce mode utilise un JWT local et un serveur WireMock pour Microsoft 365,
Azure AI Search, les embeddings et OpenAI. Les valeurs factices sont versionnées
dans les fichiers `appsettings.Local.json`; elles ne sont pas lues depuis les
`user-secrets`.

```bash
bash scripts/start-local-wiremock.sh
```

Le script fonctionne tel quel sous Linux, macOS et Windows avec Git Bash. Aucune
variable d'environnement ni fonction preparatoire n'est necessaire.

Le script :

1. démarre SQL Server et recrée la base isolée `AssistantCoreLocalDb`;
2. crée une organisation locale et son administrateur de manière idempotente;
3. démarre WireMock sur `https://localhost:9443`;
4. génère un JWT valable huit heures dans `.local/local-jwt.txt`;
5. démarre l'API et le Worker avec l'environnement `Local`.

Le JWT est également affiché au démarrage. Dans Swagger, utiliser `Authorize`,
coller uniquement le JWT, puis appeler `GET /api/core/authenticateUser` pour
valider l'identité locale.

La SPA utilise ce mode par défaut. Après le démarrage du présent script,
démarrer le dépôt `Assistant.SPA` avec `npm start`, ouvrir `/login`, puis choisir
`Continuer comme administrateur local`. La SPA récupère alors le JWT auprès de
WireMock et le conserve dans le `sessionStorage` de l'onglet. Le JWT disparaît
à la déconnexion ou lorsque l'onglet est fermé.

Dans Postman, importer la collection et l'environnement local du dossier
`postman`, puis sélectionner l'environnement `AssistantCore Local`. Aucune copie
du JWT n'est nécessaire : avant chaque requête, la collection récupère
automatiquement le token à l'adresse `https://localhost:9443/local-auth/token`
et ajoute l'en-tête `Authorization`. Cette automatisation est active uniquement
lorsque `authentication_mode` vaut `LocalJwt`.

Le mode `LocalJwt` est refusé par l'application dans tout environnement autre
que `Local`.

La base `AssistantCoreLocalDb` est supprimée et recréée à chaque lancement du
mode WireMock. La base `AssistantCoreDb`, utilisée par le mode connecté, n'est
pas réinitialisée.

### Mode local connecté aux vrais services

Ce mode conserve Microsoft Entra, Microsoft Graph, Azure AI Search et OpenAI.
Il utilise les vrais secrets configurés avec `dotnet user-secrets` comme décrit
dans la section suivante.

Dans Postman, définir `authentication_mode` à `MicrosoftEntra` pour conserver
le parcours OAuth 2.0 configuré dans la collection.

```bash
bash scripts/start-local-live.sh
```

Le script vérifie l'URL webhook publique avant de démarrer le Worker. Si elle
ne répond pas correctement, il démarre automatiquement ngrok avec l'URL
réservée de l'environnement Certif. L'agent ngrok doit donc être authentifié
localement et cette URL doit appartenir au compte utilisé.

Pour tester également le vrai parcours Microsoft dans la SPA locale, renseigner
ses identifiants publics Entra dans
`public/assets/config/config.certification.json` du dépôt `Assistant.SPA`, puis
lancer `npm run start:entra`. Aucun client secret n'est utilisé par la SPA.

## Tester un DOCX localement

Le parcours de test démarre l'API et le Worker Microsoft 365 dans le même
terminal. Le DOCX reste téléversé dans une bibliothèque SharePoint; il n'est
pas envoyé directement à l'API.

### Configuration unique

Configurer les secrets nécessaires à l'API et au Worker. Les deux projets
partagent le même magasin `user-secrets` :

```bash
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:ClientId" "<client-id>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:ClientSecret" "<secret>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:ClientStateHmacKey" "<32+ caracteres aleatoires>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:SharePointCertificatePath" "<absolute-pfx-path>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:SharePointCertificatePassword" "<pfx-password>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:EmbeddingApiKey" "<azure-openai-embedding-api-key>"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:OcrEndpoint" "https://<vision-resource>.cognitiveservices.azure.com"
dotnet user-secrets --project AssistantCore.Service set "Microsoft365:OcrApiKey" "<azure-vision-key>"
dotnet user-secrets --project AssistantCore.Service set "AzureSearch:Endpoint" "https://<service>.search.windows.net"
dotnet user-secrets --project AssistantCore.Service set "AzureSearch:IndexName" "microsoft-content-dev"
dotnet user-secrets --project AssistantCore.Service set "AzureSearch:ApiKey" "<azure-search-api-key>"
dotnet user-secrets --project AssistantCore.Service set "AzureSearch:PlanningModelApiKey" "<azure-openai-planning-api-key>"
```

`LocalLive` utilise `m365-text-embedding-3-small` pour l’indexation et la
vectorisation des requêtes. Sa Knowledge Base DEV possède un nom distinct de
celle de CERTIF. Le raisonnement `auto` est utilisé par défaut : Azure commence
par une recherche légère et active la planification si les résultats sont
insuffisants. Pour forcer un mode, définir temporairement par exemple :

```bash
AzureSearch__KnowledgeBaseRetrievalReasoningEffort=low bash scripts/start-local-live.sh
```

Les valeurs acceptées sont `minimal`, `low` et `auto`.

La connexion SQL n'a pas besoin d'être ajoutée aux `user-secrets`. Le script
lit `SQL_SERVER_PASSWORD` dans `.env.database`, construit la chaîne de
connexion en mémoire et la transmet à l'API et au Worker par variable
d'environnement.

Azure Service Bus reste désactivé en développement local. Les demandes de
synchronisation sont persistées dans SQL, puis réclamées directement par le
Worker. En environnement Azure, activer `ServiceBus:Enabled` uniquement
lorsque le namespace, les files et leurs consommateurs sont déployés.

La définition `FoundryAgent` doit pointer vers une version d’agent réellement accessible
avec la clé configurée. L'App Registration Microsoft 365 doit aussi accepter
exactement ce callback `Web` :

```text
https://localhost:7292/api/microsoft365/consent/callback
```

### Démarrage

Créer `.env.database` comme indiqué plus haut, puis exécuter :

```bash
bash scripts/start-local-live.sh
```

Le script démarre SQL Server, applique les migrations, compile la solution,
configure les valeurs locales non sensibles, puis démarre l'API et le Worker.
Utiliser `Ctrl+C` pour arrêter les deux processus.

### Parcours Postman

Importer de nouveau la collection et l'environnement Postman du dépôt, puis :

1. exécuter `AuthenticateUser` avec un membre AssistantCore `Admin`;
2. exécuter `Start Consent` et ouvrir `authorization_url` dans un navigateur;
3. renseigner `site_id`, puis exécuter `Register Site`;
4. exécuter `Get Drives`; la bibliothèque nommée par `drive_name` est placée
   automatiquement dans `drive_id`;
5. téléverser le DOCX dans cette bibliothèque SharePoint;
6. exécuter `Enable Drive` et attendre quelques secondes pendant
   l'indexation;
7. renseigner `question`, puis exécuter `Send Message`.

La réponse doit contenir le texte généré et le DOCX dans `sources`. Pour une
première synchronisation, le Worker interroge directement les tâches SQL; un
tunnel HTTPS et les webhooks ne sont nécessaires que pour recevoir ensuite les
modifications SharePoint automatiquement.

## Architecture Entra ID de developpement

### Qu'est-ce que Microsoft Entra ID?

Microsoft Entra ID est le service cloud de gestion des identites et des acces
de Microsoft. Il portait auparavant le nom `Azure Active Directory` ou
`Azure AD`.

Une entreprise peut utiliser Entra ID pour gerer :

- ses utilisateurs;
- leurs mots de passe et methodes de connexion;
- l'authentification multifacteur, aussi appelee MFA;
- les groupes et certaines permissions;
- les applications auxquelles les utilisateurs peuvent acceder;
- les politiques de securite et d'acces conditionnel;
- la connexion unique, aussi appelee Single Sign-On ou SSO.

Dans le contexte d'AssistantCore, Entra ID joue le role de fournisseur
d'identite. Cela signifie que Microsoft confirme l'identite de l'utilisateur
et remet a l'application un token signe qui contient les informations
necessaires pour identifier cet utilisateur.

AssistantCore ne recoit donc jamais le mot de passe Microsoft de
l'utilisateur. Le mot de passe, la MFA et les autres mecanismes de connexion
sont geres par Microsoft Entra ID.

Microsoft Entra ID ne doit pas etre confondu avec un abonnement Azure :

- un tenant Entra ID est un annuaire qui contient des identites et des
  applications;
- un abonnement Azure sert a payer et organiser des ressources Azure comme
  des serveurs, bases de donnees ou comptes de stockage;
- ce projet utilise actuellement Entra ID pour l'identite, meme si l'API est
  executee localement et n'est pas encore hebergee dans Azure.

### Pourquoi utiliser Entra ID dans AssistantCore?

AssistantCore est concu comme une application SaaS destinee a plusieurs
compagnies. Les utilisateurs doivent pouvoir se connecter avec le compte
professionnel deja gere par leur entreprise.

Utiliser Entra ID permet notamment :

- d'eviter de creer un systeme de mots de passe propre a AssistantCore;
- de laisser chaque compagnie gerer ses utilisateurs et ses politiques de
  connexion;
- de profiter de la MFA et des politiques de securite de la compagnie;
- d'identifier de maniere fiable l'utilisateur et son entreprise;
- de permettre une future experience SSO;
- de retirer automatiquement la possibilite de se connecter lorsque le compte
  professionnel est bloque dans Entra ID;
- de supporter plusieurs entreprises avec une seule inscription d'API
  multitenant.

Le principe general est le suivant :

```text
Utilisateur de la compagnie
        |
        | connexion Microsoft
        v
Microsoft Entra ID
        |
        | access token signe
        v
Postman, puis le futur client UI
        |
        | Authorization: Bearer <token>
        v
AssistantCore API
        |
        | tid -> organisation
        | oid -> membre
        v
AssistantCoreDb
```

En developpement, Postman remplace temporairement le futur client UI. Postman
ne valide pas lui-meme le mot de passe et ne fabrique pas le token : il ouvre
la page de connexion Microsoft, recoit le resultat du flux OAuth 2.0 et
transmet le token obtenu a l'API.

### Concepts Entra ID utilises par le projet

#### Tenant

Un tenant est l'annuaire d'une organisation dans Microsoft Entra ID.

Il contient notamment :

- les utilisateurs de l'organisation;
- les groupes;
- les inscriptions d'applications appartenant a l'organisation;
- les applications externes autorisees par l'organisation;
- les politiques de securite et de consentement.

Chaque tenant possede un identifiant unique appele `Tenant ID`. Dans un access
token, cet identifiant apparait dans le claim `tid`.

AssistantCore utilise `tid` pour determiner a quelle compagnie appartient la
requete. La valeur doit correspondre a `Organization.ExternalTenantId` dans la
base de donnees.

#### Utilisateur

Un utilisateur est un compte present dans un tenant Entra ID. Il possede un
identifiant d'objet unique dans ce tenant, appele `Object ID`.

Dans le token, cet identifiant apparait dans le claim `oid`. AssistantCore
utilise la combinaison de l'organisation et de `oid` pour trouver ou creer le
membre interne correspondant.

Deux utilisateurs appartenant a deux tenants differents peuvent avoir des
adresses similaires, mais ils restent des identites distinctes. L'email ne
doit donc pas servir seul comme identifiant de securite.

#### App registration

Une inscription d'application, ou `App registration`, decrit une application
connue par Microsoft Entra ID.

Elle definit notamment :

- son `Application (client) ID`;
- les types de comptes qui peuvent l'utiliser;
- ses Redirect URIs;
- les permissions qu'elle demande;
- les scopes qu'elle expose;
- ses secrets ou certificats lorsqu'elle est un client confidentiel.

Le projet utilise deux inscriptions :

- `AssistantCore API`, qui represente la ressource protegee;
- `AssistantCore Postman`, qui represente le client demandant un token.

Une inscription d'application est une configuration. Elle n'est ni un
utilisateur, ni le code de l'application, ni un serveur Azure.

#### Application d'entreprise et service principal

Lorsqu'un tenant client autorise une application multitenant, Entra ID cree
une representation locale de cette application dans le tenant client.

Cette representation est appelee :

- `Enterprise application` dans le portail;
- `service principal` dans le modele technique Entra ID.

L'App registration principale reste dans le tenant fournisseur. Le service
principal permet au tenant client de controler localement l'acces a cette
application sans devenir proprietaire de son inscription principale.

#### Client ID

Le Client ID est l'identifiant public d'une inscription d'application.

Il indique a Entra ID quelle application participe au flux. Ce n'est pas un
mot de passe et il peut apparaitre dans une configuration versionnee.

AssistantCore utilise deux Client IDs differents :

- `API_CLIENT_ID` identifie l'API et son audience;
- `POSTMAN_CLIENT_ID` identifie le client qui demande le token.

#### Client secret

Le Client Secret est une valeur confidentielle utilisee par un client pour
prouver son identite au endpoint de token.

Dans l'environnement actuel, le secret appartient a l'inscription
`AssistantCore Postman`. Il ne s'agit pas d'un mot de passe utilisateur et il
ne donne pas, a lui seul, acces aux donnees d'un utilisateur.

Le secret ne doit jamais etre ajoute au depot. Une application UI executee
dans un navigateur ou installee sur un appareil ne peut pas conserver un
secret de maniere fiable; le futur client UI utilisera donc PKCE plutot qu'un
secret embarque.

#### Scope et permission deleguee

Un scope represente une action qu'une application cliente demande le droit
d'effectuer.

AssistantCore expose actuellement :

```text
api://<API_CLIENT_ID>/access_as_user
```

`access_as_user` signifie que Postman appelle l'API au nom de l'utilisateur
connecte. Entra ID authentifie l'utilisateur, mais l'API doit encore decider
ce que cet utilisateur a le droit de faire.

#### Consentement

Le consentement est l'autorisation donnee a une application cliente pour
utiliser certaines permissions.

Selon les politiques du tenant, le consentement peut etre accorde :

- par l'utilisateur pour lui-meme;
- par un administrateur pour toute l'organisation.

Le consentement ne remplace pas l'authentification et ne donne pas
automatiquement le role `Admin` dans AssistantCore.

#### Access token

Un access token est une preuve temporaire signee par Microsoft Entra ID. Le
client le transmet a l'API dans le header :

```http
Authorization: Bearer <access_token>
```

Le token contient notamment :

- `aud`, la ressource a laquelle le token est destine;
- `iss`, l'autorite qui a emis le token;
- `tid`, l'identifiant du tenant;
- `oid`, l'identifiant de l'utilisateur dans ce tenant;
- des informations de profil selon les scopes demandes;
- `scp`, les permissions deleguees accordees;
- une date d'expiration.

Le token n'est pas une simple chaine d'identification. Il donne temporairement
acces a l'API avec les droits de l'utilisateur et doit etre protege comme une
information sensible.

### Responsabilites d'Entra ID et d'AssistantCore

Microsoft Entra ID est responsable de :

- connecter l'utilisateur;
- verifier ses informations de connexion;
- appliquer la MFA et les politiques Entra;
- emettre et signer le token;
- fournir les claims d'identite;
- gerer le consentement OAuth.

AssistantCore est responsable de :

- valider que le token est destine a l'API;
- retrouver l'organisation avec `tid`;
- refuser les tenants qui ne sont pas inscrits dans la plateforme;
- retrouver ou creer le membre avec `oid`;
- verifier que l'organisation et le membre sont actifs;
- attribuer et verifier les roles internes `Admin` et `User`;
- isoler les donnees entre les organisations.

Cette separation est importante : etre authentifie par Microsoft ne signifie
pas automatiquement avoir acces a AssistantCore. Le tenant doit etre inscrit
dans la base et le membre doit respecter les regles internes de l'application.

### Pourquoi utiliser deux tenants de developpement?

Le tenant fournisseur represente l'entreprise qui construit et exploite
AssistantCore. Il possede les inscriptions `AssistantCore API` et
`AssistantCore Postman`.

Le tenant client fictif represente une compagnie externe qui achete ou utilise
le produit. Il possede ses propres utilisateurs et autorise l'application
multitenant du fournisseur.

Utiliser deux tenants permet de tester un vrai scenario SaaS :

- l'utilisateur appartient reellement a une autre organisation;
- le token contient le `tid` du client fictif;
- le consentement doit etre accorde dans le tenant client;
- l'application d'entreprise est creee chez le client;
- AssistantCore doit faire correspondre ce tenant a une organisation interne;
- les donnees et les roles restent propres a cette organisation.

Tester uniquement avec le tenant fournisseur masquerait plusieurs problemes
possibles : mauvaise configuration multitenant, consentement externe absent,
mauvais `tid`, service principal manquant ou melange de donnees entre clients.

Le scenario actuel reproduit un SaaS utilise par plusieurs compagnies.

Il utilise deux tenants Microsoft Entra ID :

1. Le tenant fournisseur represente notre compagnie. Il contient les
   inscriptions d'applications de l'API et du client Postman.
2. Le tenant client represente une compagnie fictive. Il contient les
   utilisateurs qui se connectent a AssistantCore comme employes d'un client.

L'API accepte les comptes professionnels provenant de plusieurs organisations.
Le claim `tid` du token identifie la compagnie et le claim `oid` identifie
l'utilisateur dans cette compagnie.

## Creer un environnement Entra ID equivalent

Cette procedure est utile lorsqu'un developpeur veut recreer un environnement
complet et isole. Les noms affiches ci-dessous sont seulement des exemples.

### 1. Creer les tenants

Creer deux tenants Microsoft Entra ID :

- `AssistantCore Dev`, qui represente le fournisseur;
- `Contoso Test`, qui represente une compagnie cliente fictive.

Dans le tenant client, creer au moins un utilisateur de test avec lequel
effectuer la connexion.

Un tenant est un annuaire Entra ID. Un compte utilisateur appartient a un
tenant; les deux notions ne doivent pas etre confondues.

### 2. Inscrire l'API

Dans le tenant fournisseur :

1. Ouvrir le centre d'administration Microsoft Entra.
2. Selectionner le tenant fournisseur `AssistantCore Dev`.
3. Ouvrir `Identity > Applications > App registrations`.
4. Selectionner `New registration`.
5. Utiliser le nom `AssistantCore API`.
6. Dans `Supported account types`, selectionner
   `Accounts in any organizational directory`.
7. Ne pas ajouter de Redirect URI : l'API ne connecte pas directement
   l'utilisateur dans un navigateur.
8. Selectionner `Register`.
9. Dans `Overview`, noter la valeur `Application (client) ID`. Cette valeur
   sera appelee `API_CLIENT_ID` dans la suite.

L'inscription est multitenant : une compagnie cliente peut donc obtenir un
token pour cette API sans que l'inscription principale soit recreee dans son
propre tenant.

#### Exposer la permission de l'API

Dans l'inscription `AssistantCore API` :

1. Ouvrir `Expose an API`.
2. A cote de `Application ID URI`, selectionner `Add`.
3. Conserver la valeur proposee `api://<API_CLIENT_ID>` et sauvegarder.
4. Selectionner `Add a scope`.
5. Utiliser `access_as_user` comme `Scope name`.
6. Selectionner `Admins and users` pour `Who can consent` dans
   l'environnement de developpement.
7. Utiliser un nom de consentement explicite, par exemple
   `Access AssistantCore API`.
8. Expliquer dans les descriptions que l'application agit au nom de
   l'utilisateur connecte.
9. Conserver `State` a `Enabled` et selectionner `Add scope`.

Le nom complet de la permission devient :

```text
api://<API_CLIENT_ID>/access_as_user
```

Il s'agit d'une permission deleguee : l'application cliente appelle l'API au
nom d'un utilisateur connecte. Il ne s'agit pas du flux `Client Credentials`,
qui representerait une application sans utilisateur.

Reporter l'identifiant de l'API dans `AssistantCore.Service/appsettings.json` :

```json
"AzureAd": {
  "Instance": "https://login.microsoftonline.com/",
  "TenantId": "organizations",
  "ClientId": "<API_CLIENT_ID>",
  "Audience": "<API_CLIENT_ID>"
}
```

Les identifiants de client et de tenant ne sont pas des secrets. Les secrets,
certificats et mots de passe ne doivent jamais etre ajoutes au depot.

### 3. Inscrire le client Postman

Le projet ne possede pas encore de client UI. Postman joue temporairement le
role de l'application cliente : il ouvre la connexion Microsoft, recupere un
code d'autorisation, l'echange contre un access token et utilise ce token pour
appeler AssistantCore.

Toujours dans le tenant fournisseur `AssistantCore Dev` :

1. Retourner dans `App registrations` et selectionner `New registration`.
2. Utiliser le nom `AssistantCore Postman`.
3. Dans `Supported account types`, selectionner
   `Accounts in any organizational directory`.
4. Dans `Redirect URI`, selectionner la plateforme `Web`.
5. Entrer exactement `https://oauth.pstmn.io/v1/callback`.
6. Selectionner `Register`.
7. Dans `Overview`, noter la valeur `Application (client) ID`. Cette valeur
   sera appelee `POSTMAN_CLIENT_ID`.

L'inscription Postman est distincte de l'inscription API :

- `AssistantCore API` represente la ressource protegee qui valide le token;
- `AssistantCore Postman` represente le client qui demande et utilise le token.

Le `POSTMAN_CLIENT_ID` doit etre utilise dans le champ `Client ID` de Postman.
Le `API_CLIENT_ID` sert dans le scope et dans la configuration du backend.
Intervertir les deux identifiants produit un token avec une mauvaise audience
ou une erreur de consentement.

#### Creer le client secret Postman

Dans l'inscription `AssistantCore Postman` :

1. Ouvrir `Certificates & secrets`.
2. Ouvrir l'onglet `Client secrets`.
3. Selectionner `New client secret`.
4. Utiliser une description comme `Postman local development`.
5. Choisir une expiration courte adaptee au developpement.
6. Selectionner `Add`.
7. Copier immediatement la colonne `Value`.

La `Value` est le `POSTMAN_CLIENT_SECRET`. Elle n'est affichee qu'au moment de
la creation. La colonne `Secret ID` est seulement l'identifiant administratif
du secret et ne peut pas etre utilisee pour obtenir un token.

Le secret prouve l'identite de l'application Postman pendant l'echange du code
d'autorisation. Il doit rester dans les valeurs locales de Postman : ne jamais
l'ajouter au README, a la collection exportee, a un ticket, a une capture
d'ecran ou a Git. Creer un nouveau secret lorsque celui-ci expire ou est
expose, puis supprimer l'ancien dans Entra ID.

Cette configuration avec secret reproduit l'environnement Postman actuel. Le
futur client UI, surtout s'il s'agit d'une SPA ou d'une application installee,
ne devra pas embarquer ce secret. Il devra utiliser Authorization Code avec
PKCE comme client public.

#### Autoriser Postman a appeler l'API

Dans l'inscription `AssistantCore Postman` :

1. Ouvrir `API permissions`.
2. Selectionner `Add a permission`.
3. Selectionner `My APIs`.
4. Selectionner `AssistantCore API`.
5. Selectionner `Delegated permissions`.
6. Cocher `access_as_user` et selectionner `Add permissions`.
7. Selectionner `Grant admin consent` pour le tenant fournisseur et confirmer.

La permission declare ce que Postman peut demander. Le consentement autorise
effectivement cette application a agir au nom des utilisateurs pour ce scope.

Documentation officielle :

- https://learn.microsoft.com/entra/identity-platform/quickstart-configure-app-expose-web-apis
- https://learn.microsoft.com/entra/identity-platform/single-and-multi-tenant-apps
- https://learning.postman.com/docs/use/authorization/oauth-20/

### 4. Autoriser le tenant client

L'inscription des applications reste dans le tenant fournisseur. Le tenant
client recoit plutot une application d'entreprise, aussi appelee service
principal, lorsqu'il accorde son consentement.

Dans le tenant client `Contoso Test` :

1. Ouvrir `Identity > Overview` et copier `Tenant ID`. Cette valeur sera
   appelee `TENANT_CLIENT_ID`.
2. Creer au moins deux utilisateurs de test, par exemple un futur
   administrateur et un utilisateur standard.
3. Dans Postman, commencer la demande d'un token avec un administrateur du
   tenant client.
4. Accepter les permissions demandees et consentir pour l'organisation si le
   compte possede les droits necessaires.
5. Verifier ensuite dans `Enterprise applications` que l'application cliente
   existe dans le tenant.

Si les politiques du tenant bloquent le consentement utilisateur, un
administrateur Entra doit accorder explicitement le consentement. Cette etape
est distincte du role `Admin` stocke dans AssistantCore : un administrateur
Entra gere l'annuaire Microsoft, alors qu'un administrateur AssistantCore gere
les membres de son organisation dans l'application.

## Enregistrer une compagnie cliente dans la base

Un token Entra ID valide ne suffit pas. Le tenant client doit aussi correspondre
a une organisation active dans AssistantCore.

Ouvrir SQLPad sur `http://localhost:3000` et executer :

```sql
USE [AssistantCoreDb];

INSERT INTO [dbo].[Organization]
(
    [Id],
    [Name],
    [IdentityProvider],
    [ExternalTenantId],
    [Status]
)
VALUES
(
    NEWID(),
    N'Contoso Test',
    N'MicrosoftEntraId',
    N'<TENANT_CLIENT_ID>',
    N'Actif'
);
```

`ExternalTenantId` doit correspondre exactement au claim `tid` du token.
Sinon, `GET /api/core/authenticateUser` retourne `403 Forbidden`.

## Tester avec Postman

### 1. Importer les fichiers

Importer dans Postman :

- `postman/AssistantCore.postman_collection.json`;
- `postman/AssistantCore.local.postman_environment.json`.

Selectionner ensuite l'environnement `AssistantCore Local`.

### 2. Configurer les variables

Dans l'environnement `AssistantCore Local`, renseigner les valeurs locales :

```text
base_url=https://localhost:7292
tenant_id=<TENANT_CLIENT_ID>
api_client_id=<API_CLIENT_ID>
postman_client_id=<POSTMAN_CLIENT_ID>
postman_client_secret=<POSTMAN_CLIENT_SECRET>
scope=api://<API_CLIENT_ID>/access_as_user openid profile email
```

`tenant_id` correspond au tenant de la compagnie fictive, pas au tenant
fournisseur. Il sert a enregistrer et diagnostiquer la compagnie cliente. Les
URLs OAuth utilisent volontairement `organizations` pour accepter les comptes
professionnels de tous les tenants Microsoft Entra ID.

Marquer `postman_client_secret` comme variable de type `secret` et enregistrer
sa valeur uniquement dans la valeur locale de l'environnement. Avant de
partager ou d'exporter un environnement Postman, verifier que cette valeur
n'est pas incluse.

### 3. Configurer OAuth 2.0

Ouvrir la collection, puis `Authorization`. Utiliser les valeurs suivantes :

| Champ Postman | Valeur |
|---|---|
| Type | `OAuth 2.0` |
| Add auth data to | `Request Headers` |
| Token Name | `AssistantCore local` |
| Grant Type | `Authorization Code` |
| Callback URL | `https://oauth.pstmn.io/v1/callback` |
| Auth URL | `https://login.microsoftonline.com/organizations/oauth2/v2.0/authorize` |
| Access Token URL | `https://login.microsoftonline.com/organizations/oauth2/v2.0/token` |
| Client ID | `{{postman_client_id}}` |
| Client Secret | `{{postman_client_secret}}` |
| Scope | `{{scope}}` |
| State | `assistantcore-bootstrap` |
| Client Authentication | `Send client credentials in body` |

Signification des champs :

- `Grant Type` indique que l'utilisateur se connecte dans un navigateur. Entra
  retourne d'abord un code temporaire, jamais directement le token a l'API.
- `Callback URL` est l'adresse vers laquelle Entra renvoie le code. Elle doit
  correspondre exactement, protocole et chemin inclus, a l'URI `Web`
  enregistree dans `AssistantCore Postman`.
- `Auth URL` est le endpoint interactif qui affiche la connexion Microsoft et
  recueille le consentement.
- `Access Token URL` est le endpoint appele par Postman pour echanger le code
  contre un access token.
- `organizations` accepte les comptes professionnels ou scolaires provenant
  de n'importe quel tenant Entra ID, mais exclut les comptes Microsoft
  personnels.
- `Client ID` identifie publiquement l'inscription `AssistantCore Postman`.
- `Client Secret` prouve l'identite du client Postman. Contrairement au Client
  ID, cette valeur est confidentielle.
- `Scope` est une liste separee par des espaces. Le scope
  `api://<API_CLIENT_ID>/access_as_user` demande un access token pour
  AssistantCore. `openid` active OpenID Connect, `profile` demande les claims
  de profil et `email` demande l'adresse courriel lorsqu'elle existe.
- `State` est une valeur retournee sans modification apres la connexion afin
  de lier la reponse a la demande initiale et limiter les attaques de
  substitution de requete.
- `Send client credentials in body` envoie `client_id` et `client_secret` dans
  le corps du POST vers `/token` au lieu d'utiliser un header Basic.
- `Request Headers` fait heriter les requetes de la collection du header
  `Authorization: Bearer <access_token>`.

### 4. Obtenir un token

Dans l'onglet `Authorization` de la collection :

1. Selectionner `Get New Access Token`.
2. Activer `Authorize using browser` si Postman le propose.
3. Se connecter avec un utilisateur du tenant client fictif, pas avec un
   utilisateur du tenant fournisseur.
4. Accepter le consentement si Entra ID le demande.
5. Verifier que Postman a recu un access token.
6. Selectionner `Use Token`.

Pendant ce flux :

1. Postman ouvre l'Auth URL avec le Client ID, le callback et les scopes.
2. Entra ID authentifie l'utilisateur et retourne un code au callback Postman.
3. Postman envoie le code, le Client ID et le Client Secret a l'Access Token
   URL.
4. Entra ID retourne un access token destine a AssistantCore.
5. Postman ajoute ce token comme Bearer token aux requetes de la collection.

Le backend valide notamment la signature, l'audience et l'emetteur du token.
Il utilise ensuite le claim `tid` pour trouver l'organisation et le claim
`oid` pour trouver ou creer le membre. Ne pas partager un token dans un ticket
ou un outil public : un access token actif permet d'appeler l'API avec les
droits de l'utilisateur.

### 5. Routes de la collection

Toutes les routes AssistantCore heritent du token OAuth 2.0 configure au niveau
de la collection.

| Requete Postman | Methode et route | Resultat attendu |
|---|---|---|
| `AuthenticateUser` | `GET /api/core/authenticateUser` | Valide l'identite, retrouve l'organisation et cree le membre avec le role `User` s'il n'existe pas. |
| `Get Members` | `GET /api/members` | Retourne les membres de l'organisation courante. Le membre connecte doit avoir le role `Admin`. |
| `Update Member Role` | `PATCH /api/members/{{member_id}}/role` | Remplace le role du membre cible par `Admin` ou `User`. Le membre connecte doit avoir le role `Admin`. |

`AuthenticateUser` enregistre automatiquement `current_user_id` dans
l'environnement. `Get Members` cherche un autre membre actif et enregistre son
identifiant dans `member_id`. `Update Member Role` utilise `member_id` et la
valeur `member_role` dans le body suivant :

```json
{
  "role": "{{member_role}}"
}
```

Les valeurs acceptees pour `member_role` sont `Admin` et `User`.

La requete `GET https://graph.microsoft.com/v1.0/me` presente dans certaines
collections locales n'est pas une route AssistantCore. Elle exige une
permission Microsoft Graph deleguee telle que `User.Read` et un access token
dont l'audience est Microsoft Graph. Le token demande avec
`api://<API_CLIENT_ID>/access_as_user` est destine a AssistantCore et ne doit
pas etre reutilise pour appeler Microsoft Graph.

### 6. Scenario de test complet

Executer les requetes dans cet ordre :

1. Obtenir un token avec le premier utilisateur du tenant client.
2. Executer `AuthenticateUser`. Le backend cree ce membre avec le role `User`.
3. Promouvoir ce premier membre en `Admin` avec SQLPad :

```sql
USE [AssistantCoreDb];

UPDATE [dbo].[OrganizationMember]
SET [Role] = N'Admin'
WHERE [Email] = N'<EMAIL_UTILISATEUR_TEST>';
```

4. Obtenir un nouveau token avec le deuxieme utilisateur du meme tenant.
5. Executer `AuthenticateUser` afin de creer ce deuxieme membre avec le role
   `User`.
6. Obtenir de nouveau un token avec le premier utilisateur administrateur.
7. Executer `AuthenticateUser`, puis `Get Members`. La collection place
   l'identifiant du deuxieme membre dans `member_id`.
8. Choisir `Admin` ou `User` dans `member_role`.
9. Executer `Update Member Role` et verifier le code `200` ainsi que le role
   retourne.

Erreurs courantes :

- `401 Unauthorized` : token absent, expire, signature invalide ou mauvaise
  audience;
- `403 Forbidden` sur `AuthenticateUser` : le `tid` du token ne correspond a
  aucune organisation active dans la base;
- `403 Forbidden` sur les routes membres : le membre connecte n'a pas le role
  interne `Admin`;
- `AADSTS50011` : le Callback URL Postman ne correspond pas exactement a l'URI
  configuree dans Entra ID;
- `invalid_client` : mauvais Client ID, secret invalide ou secret expire;
- erreur de consentement : la permission `access_as_user` n'a pas ete ajoutee
  au client Postman ou autorisee dans le tenant concerne.

## Tests disponibles

Compilation du projet :

```bash
dotnet build Solution.sln
```

Tests manuels d'integration :

- connexion Entra ID depuis le tenant client;
- validation du token par l'API;
- creation automatique du membre;
- isolation de l'organisation avec le claim `tid`;
- consultation et modification des membres avec un administrateur;
- scripts de validation inclus dans la collection Postman.

Le projet contient des tests unitaires pour le flux `AuthenticateUser`, le
controleur `CoreController` et `ExceptionMiddleware`. Ils couvrent les regles
de creation et d'acces des membres, la construction de la reponse, la
delegation du controleur et la traduction des exceptions en reponses HTTP.

Executer tous les tests automatises :

```bash
dotnet test Solution.sln
```

## Arreter l'environnement

Arreter les conteneurs sans supprimer les donnees :

```bash
docker compose --env-file .env.database down
```

Supprimer aussi les volumes et reinitialiser la base :

```bash
docker compose --env-file .env.database down --volumes
```

Cette derniere commande supprime les donnees locales SQL Server et SQLPad.

## Strategie recommandee pour l'equipe

A court terme, chaque developpeur peut recreer les deux tenants pour disposer
d'un environnement completement isole.

A moyen terme, l'approche recommandee est de maintenir :

- un tenant fournisseur de developpement commun;
- un tenant client fictif commun;
- des inscriptions d'applications dediees a l'environnement de developpement;
- un compte nominatif par developpeur dans le tenant client;
- des comptes de test generiques seulement pour les scenarios automatises;
- les secrets et acces de recuperation dans un gestionnaire de secrets d'equipe.

Un identifiant et un mot de passe uniques partages par toute l'equipe sont
deconseilles : ils compliquent la MFA, la tracabilite et la revocation d'acces.
