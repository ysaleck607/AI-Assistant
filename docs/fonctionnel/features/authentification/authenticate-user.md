# Authenticate User

## Table des matieres

- [But](#but)
- [Quand appeler cet endpoint](#quand-appeler-cet-endpoint)
- [Reponse du frontend](#ce-que-le-frontend-doit-recevoir)
- [Donnees de depart](#donnees-de-depart)
- [Etapes du flow complet](#auth-flow)
- [Provisionnement automatique](#auth-member-provisioning)
- [Autorisation OAuth](#auth-oauth-authorization)
- [Politique d'admission](#auth-admission-policy)
- [Configuration Microsoft Entra](#auth-entra-configuration)
- [Identite stable du membre](#auth-member-identity)
- [Derniere connexion](#auth-last-login)
- [Erreurs](#erreurs-a-prevoir)
- [Regles metier](#regles-metier-fixes-pour-cet-endpoint)
- [References](#references-techniques)

## But

`authenticateUser` sert a construire la session applicative complete d'un utilisateur deja connecte.

Le but est simple :
quand le frontend appelle cet endpoint, il doit recevoir toutes les informations necessaires pour ouvrir l'application dans le bon contexte.

Cet endpoint ne fait pas la connexion elle-meme.
Il travaille apres la connexion.

---

## Quand appeler cet endpoint

Le frontend appelle `authenticateUser` :
- juste apres la connexion
- au chargement initial de l'application
- apres un refresh
- quand il faut reconstruire la session utilisateur

---

## Ce que le frontend doit recevoir

La reponse doit permettre au frontend de savoir :
- quel utilisateur est connecte
- a quelle entreprise il appartient
- quel role il a

Exemple de retour :

```json
{
  "user": {
    "id": "a7c2d5b1-9b5d-4a8d-8b9f-2c4d6e8f2001",
    "displayName": "Marc Tremblay",
    "email": "marc.tremblay@josetchibozo2hotmail.onmicrosoft.com"
  },
  "organization": {
    "id": "5e1d4f9a-4e68-4f35-9a9e-7d4d2f6c1001",
    "name": "MetalPro"
  },
  "roles": [
    "User"
  ]
}
```

---

## Donnees de depart

Cet endpoint ne recoit pas de body metier.

Il lit les informations deja presentes dans la session ou dans le token d'authentification.

Le backend doit pouvoir recuperer au minimum :
- l'identifiant externe de l'utilisateur
- l'email
- le nom ou nom affiche
- l'identifiant externe de l'entreprise ou du tenant

---

<a id="auth-flow"></a>
## Etapes de construction fonctionnelle

### 1. Verifier que l'utilisateur est bien connecte

Le backend commence par verifier que la requete vient d'un utilisateur authentifie.

Concretement :
- verifier qu'un token ou une session existe
- verifier que cette session est valide
- verifier que le contexte de securite contient bien une identite utilisateur

Si ce controle echoue :
- retourner `401 Unauthorized`
- ne rien faire de plus

Cette etape sert a eviter de construire une session applicative pour un utilisateur inconnu.

---

### 2. Lire les informations d'identite dans la session

Le backend lit les informations deja disponibles dans le contexte d'authentification.

Concretement, il faut extraire :
- `externalUserId`
- `externalTenantId`
- `email`
- `displayName` ou les champs de nom disponibles

Le but est de recuperer une identite fiable sans demander ces informations au frontend.

Si `externalUserId` manque :
- impossible d'identifier l'utilisateur
- il faut refuser la demande

Si `externalTenantId` manque :
- impossible de savoir a quelle entreprise rattacher la session
- il faut refuser la demande

Le frontend ne doit pas envoyer lui-meme ces valeurs comme source de verite.

---

### 3. Retrouver l'entreprise interne

Le backend utilise `externalTenantId` pour retrouver l'entreprise correspondante dans la plateforme.

Concretement :
- chercher dans la base l'entreprise qui correspond a cet identifiant externe
- charger sa fiche interne
- recuperer son identifiant interne et son nom

Si aucune entreprise n'est trouvee :
- retourner `403 Forbidden`

Cela veut dire :
l'utilisateur est bien connecte chez le fournisseur d'identite, mais son entreprise n'existe pas encore dans la plateforme.

---

### 4. Verifier que l'entreprise est active

Le backend doit confirmer que l'entreprise a le droit d'utiliser la plateforme.

Concretement :
- lire le statut de l'entreprise
- verifier qu'il autorise l'acces

Exemples de statuts qui bloquent :
- suspendue
- desactivee
- archivee

Si l'entreprise n'est pas active :
- retourner `403 Forbidden`
- arreter le traitement

Cette verification doit arriver tot dans le flux pour ne pas charger inutilement le reste.

---

### 5. Chercher l'utilisateur interne avec son identite externe

Le backend verifie si un utilisateur interne existe deja pour cette entreprise.

Concretement :
- chercher un utilisateur avec `externalUserId`
- verifier qu'il appartient bien a l'entreprise trouvee a l'etape precedente

Il faut faire cette recherche dans la bonne organisation pour eviter tout melange entre entreprises.

Si l'utilisateur existe :
- charger son compte interne
- continuer

Si l'utilisateur n'existe pas :
- passer a l'etape suivante pour le creer automatiquement

---

<a id="auth-member-provisioning"></a>
### 6. Creer automatiquement l'utilisateur si le compte n'existe pas

Dans ce projet, la regle est simple :
si l'utilisateur n'existe pas, le backend doit le creer automatiquement.

Concretement, il faut :
- creer un nouvel utilisateur interne
- rattacher cet utilisateur a l'organisation trouvee
- enregistrer son `externalUserId`
- enregistrer son email
- enregistrer son `displayName`
- definir son statut initial a `Actif`
- enregistrer a titre indicatif le role derive de ses app roles Entra au moment de la creation (voir [Politique d'admission](#auth-admission-policy))

Le role enregistre a la creation n'est jamais code en dur : il vient du resolveur de role, qui lit les app roles Entra du token (`AssistantCore.Access` et, le cas echeant, `TenantAdmin`). Cette valeur en base est informative et ne sert pas a autoriser les actions du membre.

Le point important :
un utilisateur authentifie peut entrer dans la plateforme meme s'il n'existait pas encore en base, parce que la creation automatique est autorisee.

Deux appels simultanes peuvent chercher le meme utilisateur avant que sa
creation soit terminee. La base doit garantir qu'un seul membre existe pour la
meme organisation, le meme fournisseur d'identite et le meme identifiant
externe.

Si une requete perd cette course parce que l'autre a cree le membre en premier,
elle recharge le membre cree et continue seulement s'il appartient a
l'organisation attendue et qu'il est actif. Une autre erreur de base de donnees
ne doit pas etre presentee comme une creation concurrente reussie.

---

<a id="auth-oauth-authorization"></a>
## Autorisation OAuth de l'API

Un token valide prouve l'identite de l'appelant, mais il ne suffit pas a autoriser l'utilisation d'AssistantCore.

Tous les endpoints appeles au nom d'un utilisateur doivent verifier les deux permissions suivantes :

- le scope delegue `access_as_user`
- le role d'admission Entra `AssistantCore.Access`

Ces permissions ont des responsabilites differentes :

| Controle | Signification |
| --- | --- |
| `access_as_user` | L'application cliente peut appeler l'API au nom de l'utilisateur connecte |
| `AssistantCore.Access` | L'utilisateur a ete admis sur la plateforme par son organisation |
| Role `Admin` ou `User` conserve en base | Information historique ou d'affichage; ne donne aucune autorisation |
| App role Entra `TenantAdmin` | L'organisation cliente designe ce membre comme administrateur effectif de son espace AssistantCore |

Le role Entra `AssistantCore.Access` reste uniquement une preuve d'admission : il ne donne jamais `Admin` a lui seul. L'app role Entra `TenantAdmin` (distinct de `AssistantCore.Access`) est la source d'autorite pour les actions d'administration : voir [Politique d'admission](#auth-admission-policy) pour la regle exacte de derivation.

Seul un app role definit sur l'App Registration d'AssistantCore compte pour cette derivation. Un role natif Microsoft comme `Global Administrator` du tenant client n'est jamais utilise pour deduire `TenantAdmin`.

Le backend doit verifier, dans cet ordre :

1. la signature, l'emetteur, l'audience et l'expiration du token
2. la presence du scope `access_as_user` dans le claim `scp`
3. la presence de `AssistantCore.Access` dans le claim `roles`
4. la presence des claims `tid` et `oid`
5. l'existence et le statut actif de l'organisation interne
6. l'existence ou le provisionnement du membre interne et la derivation du role effectif depuis `roles` (voir [Politique d'admission](#auth-admission-policy))
7. le statut actif du membre

Un token absent ou invalide retourne `401 Unauthorized`.

Un token valide sans le scope ou le role d'admission attendu retourne `403 Forbidden`.

Un token applicatif sans utilisateur ne peut pas appeler les endpoints utilisateur. Les appels de workers ou de jobs utiliseront plus tard des permissions applicatives et des endpoints separes.

---

<a id="auth-admission-policy"></a>
## Politique d'admission et de provisionnement

Pour le MVP, une organisation doit etre creee et activee dans AssistantCore avant que ses utilisateurs puissent acceder a la plateforme.

L'administrateur Microsoft Entra du client decide qui peut entrer dans AssistantCore. Il affecte les utilisateurs ou groupes autorises au role Entra `AssistantCore.Access` de l'Enterprise Application.

Ce meme administrateur Entra du client gere aussi les droits d'administration de la plateforme, via un second app role, `TenantAdmin`, defini sur l'App Registration d'AssistantCore. AssistantCore ne gere pas ces droits depuis la base : le jeton courant est la source d'autorite. Ce choix fait qu'un retrait de `TenantAdmin` prend effet des qu'un nouveau jeton est utilise, sans attendre une synchronisation en base.

Regle de derivation du role effectif de la session, appliquee a chaque requete autorisee :

- absence de `AssistantCore.Access` dans `roles` -> acces refuse (`403`)
- `AssistantCore.Access` seul -> role effectif `User`
- `AssistantCore.Access` et `TenantAdmin` -> role effectif `Admin`
- `TenantAdmin` sans `AssistantCore.Access` -> acces refuse (`403`), `TenantAdmin` seul ne suffit jamais

Un utilisateur peut etre provisionne automatiquement seulement si :

- son token est valide et destine a AssistantCore
- son token contient `access_as_user`
- son token contient `AssistantCore.Access`
- son `tid` correspond a une organisation interne active
- son `oid` identifie un utilisateur supporte
- le membre interne n'existe pas encore pour cette organisation et cet `oid`

Le membre cree automatiquement recoit toujours :

- le role derive de la regle ci-dessus (`Admin` ou `User`), conserve a titre indicatif
- le statut interne `Active`
- l'organisation determinee depuis `tid`
- l'identite externe determinee depuis `oid`

A chaque requete, le backend derive de nouveau le role effectif du jeton courant. Il n'ecrit pas ce role dans la fiche du membre existant. La valeur conservee en base reste indicative et ne peut ni accorder ni retirer une autorisation.

### Admission conditionnee a la configuration Microsoft 365

`TenantAdmin` sert a demarrer la configuration Microsoft 365 d'une organisation, pas a rester une autorisation obligatoire pour tous les membres. La regle d'admission generale distingue donc deux periodes :

- **Configuration incomplete** (consentement Microsoft 365 non valide ou aucun site selectionne) : `AssistantCore.Access` reste obligatoire pour tous, et `TenantAdmin` est en plus exige. Un membre standard qui ne l'a pas ne peut ni ouvrir le chat ni utiliser les endpoints Microsoft 365 ; le backend retourne `403 Forbidden` avec le code metier `tenant_admin_required`, y compris sur `authenticateUser` lui-meme, pour que le frontend puisse afficher qu'un administrateur doit terminer la configuration.
- **Configuration terminee** : `AssistantCore.Access` reste obligatoire, mais `TenantAdmin` n'est plus une condition d'admission. Un membre standard accede normalement au SaaS et au chat. Le jeton d'un `TenantAdmin` lui donne toujours les droits d'administration, sans que cela depende du role conserve en base.

Cette regle s'applique a chaque point d'entree qui resout l'organisation et le membre courants (`authenticateUser`, les endpoints Microsoft 365, et les endpoints de messages/conversations), pas seulement dans le frontend. La definition de "configuration terminee" est unique dans tout le systeme : `IsConsentComplete && HasSelectedSite && HasIndexedSource`, la meme que celle exposee par `GET /api/microsoft365/onboarding` (voir [Indexer les documents SharePoint dans Azure AI Search](../microsoft365/index-sharepoint-content.md)). Une ligne de site creee avant un echec de decouverte ne suffit donc pas a ouvrir l'acces aux membres standards.

Les utilisateurs invites peuvent être autorisés dans la première version
seulement s’ils possèdent un objet invité dans le tenant Microsoft Entra du
client et sont affectés explicitement à `AssistantCore.Access`.

Le claim `tid` doit correspondre au tenant client qui héberge l’objet invité.
Le claim `oid` doit correspondre à l’identifiant de cet objet invité dans ce
tenant. L’adresse courriel ou le tenant d’origine de l’invité ne remplace
jamais ces identifiants.

Le retrait de `AssistantCore.Access`, la désactivation du membre interne ou la
suppression de l’objet invité retire l’accès à AssistantCore.

### Retirer l'acces

Pour retirer l'acces a un utilisateur :

1. un administrateur AssistantCore desactive le membre interne pour bloquer les appels dans la plateforme
2. un administrateur Entra retire l'affectation `AssistantCore.Access`
3. l'utilisateur ne peut plus obtenir de nouveau token autorise
4. le membre reste en base pour conserver son historique

Pour un retrait urgent, la desactivation interne doit etre faite en premier, car un token deja emis peut rester valide jusqu'a son expiration.

La désactivation interne utilise
`PATCH /api/members/{memberId}/status`, décrit dans
[Gérer le statut d'un membre](../membres/manage-member-status.md).

Retirer uniquement `TenantAdmin` (en laissant `AssistantCore.Access`) ne retire pas l'acces a la plateforme : avec un nouveau jeton, le membre a simplement le role effectif `User`, sans intervention manuelle cote AssistantCore.

---

<a id="auth-entra-configuration"></a>
## Configuration Microsoft Entra etape par etape

Cette configuration comporte une partie geree par Sypnatix dans l'App Registration principale et une partie realisee dans chaque tenant client.

### A. Creer le role d'admission dans l'App Registration

Dans le tenant qui possede l'App Registration AssistantCore :

1. ouvrir `Microsoft Entra admin center`
2. ouvrir `Entra ID`
3. ouvrir `App registrations`
4. selectionner l'App Registration de l'API AssistantCore
5. ouvrir `App roles`
6. cliquer sur `Create app role`
7. utiliser `AssistantCore Access` comme nom affiche
8. selectionner `Users/Groups` dans les types de membres autorises
9. utiliser exactement `AssistantCore.Access` comme valeur
10. ajouter une description indiquant que ce role autorise l'acces a la plateforme sans donner un role metier interne
11. activer le role puis enregistrer

Le role est defini sur l'API afin d'apparaitre dans le token d'acces destine a AssistantCore.

### A2. Creer le role d'administration dans l'App Registration

Dans la meme App Registration, repeter les etapes precedentes pour un second app role :

1. cliquer sur `Create app role`
2. utiliser `AssistantCore Tenant Admin` comme nom affiche
3. selectionner `Users/Groups` dans les types de membres autorises
4. utiliser exactement `TenantAdmin` comme valeur
5. ajouter une description indiquant que ce role donne les droits d'administration AssistantCore, en plus de l'admission
6. activer le role puis enregistrer

`TenantAdmin` ne remplace jamais `AssistantCore.Access` : un membre doit toujours avoir les deux pour obtenir le role effectif `Admin` (voir [Politique d'admission](#auth-admission-policy)).

### B. Verifier le scope delegue

Dans la meme App Registration :

1. ouvrir `Expose an API`
2. verifier que l'Application ID URI est configure
3. verifier que le scope `access_as_user` existe et est actif
4. verifier que l'application cliente demande `api://<API_CLIENT_ID>/access_as_user`
5. accorder le consentement administrateur lorsque la politique du tenant l'exige

### C. Activer l'affectation obligatoire dans le tenant client

Un administrateur du tenant client effectue les operations suivantes :

1. ouvrir `Microsoft Entra admin center`
2. ouvrir `Entra ID`
3. ouvrir `Enterprise applications`
4. rechercher et selectionner `AssistantCore`
5. ouvrir `Properties`
6. configurer `Assignment required?` a `Yes`
7. enregistrer

Cette operation evite qu'un utilisateur non affecte utilise l'Enterprise Application uniquement parce qu'il appartient au tenant.

### D. Affecter les utilisateurs ou groupes autorises

Dans l'Enterprise Application AssistantCore du tenant client :

1. ouvrir `Users and groups`
2. cliquer sur `Add user/group`
3. selectionner les utilisateurs ou groupes autorises
4. selectionner le role `AssistantCore.Access`
5. cliquer sur `Assign`
6. verifier que chaque affectation apparait avec le bon role
7. pour les membres qui doivent administrer AssistantCore, repeter l'affectation avec le role `TenantAdmin`, en plus de `AssistantCore.Access`

Ne pas affecter de service principal ou de groupe dont le périmètre n’est pas
maîtrisé. Un compte invité doit être affecté individuellement ou par un groupe
explicitement approuvé.

### E. Verifier le token et le premier acces

Avec un utilisateur de test affecte :

1. demander un token pour le scope `access_as_user`
2. verifier que le token vise l'audience de l'API AssistantCore
3. verifier que `scp` contient `access_as_user`
4. verifier que `roles` contient `AssistantCore.Access`
5. appeler `authenticateUser`
6. verifier que le membre est cree avec un role indicatif `User` si `roles` ne contient pas `TenantAdmin`, ou `Admin` s'il le contient
7. rappeler l'endpoint et verifier qu'aucun doublon n'est cree
8. affecter ou retirer `TenantAdmin` a cet utilisateur dans le tenant client, obtenir un nouveau jeton, rappeler `authenticateUser` et verifier que les droits effectifs changent sans ecriture du role en base

Avec un utilisateur non affecte, verifier que l'acces est refuse et qu'aucun membre interne n'est cree.

---

<a id="auth-member-identity"></a>
## Identite stable et profil du membre

La cle d'identite stable d'un membre est composee de :

- l'identifiant interne de l'organisation
- le fournisseur d'identite
- le claim Entra `oid`

Le claim `tid` determine l'organisation candidate. Le claim `oid` identifie l'utilisateur dans ce tenant.

L'email et le nom affiche sont des donnees de profil. Ils ne doivent jamais servir a prendre une decision d'autorisation ou a retrouver seuls un membre.

Lors d'une authentification reussie, le backend peut actualiser le nom et l'email d'un membre existant lorsque les nouvelles valeurs sont valides. Cette synchronisation ne doit jamais modifier :

- le statut interne
- l'organisation
- l'identifiant externe

Le role indicatif conserve en base n'est pas actualise lors des authentifications suivantes. Le role effectif est derive du jeton courant et utilise uniquement pour la session et ses autorisations (voir [Politique d'admission](#auth-admission-policy)).

Un changement d'email ne doit pas creer un second membre. Deux identites externes differentes ne doivent pas etre fusionnees uniquement parce qu'elles presentent le meme email.

La contrainte d'unicite principale doit porter sur l'organisation, le fournisseur d'identite et l'identifiant utilisateur externe.

---

### 7. Verifier que le compte utilisateur est actif

Meme si le compte existe ou vient d'etre cree, le backend doit verifier son statut interne.

Concretement :
- lire le statut du compte utilisateur
- verifier qu'il est actif

Si le compte est suspendu ou desactive :
- retourner `403 Forbidden`

Cette etape permet de bloquer un utilisateur qui serait valide dans le systeme externe mais interdit localement dans la plateforme.

---

### 8. Deriver le role effectif de l'utilisateur

Le tenant Entra du client est la source d'autorite pour les droits d'administration. Le role conserve dans la base est indicatif et ne participe jamais a cette decision.

Concretement :
- deriver le role effectif a partir des app roles Entra du token, avec la regle de [Politique d'admission](#auth-admission-policy)
- utiliser ce role pour la reponse et les autorisations de la requete courante
- ne pas modifier le role indicatif du membre en base

Les roles possibles restent seulement :
- `Admin`
- `User`

---

### 9. Construire la reponse finale

Le backend construit ensuite une reponse stable et simple a consommer.

Concretement, la reponse doit contenir :
- `user`
- `organization`
- `roles`

Le champ `user` contient les infos utiles pour afficher et identifier l'utilisateur.
Le champ `organization` contient les infos utiles sur l'entreprise active.
Le champ `roles` contient le role effectif derive du jeton pour la session courante.

Le backend ne doit jamais retourner :
- mot de passe
- secret
- token sensible
- cle API
- donnees d'une autre entreprise

---

<a id="auth-last-login"></a>
### 10. Enregistrer la derniere connexion

Cette date indique la derniere fois ou un membre a reussi a construire sa
session AssistantCore complete.

Elle permet notamment :

- de distinguer un membre cree automatiquement qui n'a jamais termine ce flow;
- d'aider le support a verifier qu'un membre a recemment accede a
  AssistantCore;
- de confirmer que le tenant, l'organisation, l'admission et le membre ont
  tous ete valides.

Elle ne constitue pas une preuve d'activite metier. Le frontend peut appeler
`authenticateUser` lors d'une connexion, d'un chargement ou d'un
rafraichissement de page, meme si le membre n'effectue ensuite aucune action.

Sans cette date, AssistantCore connait l'existence du membre, mais ne peut pas
savoir s'il a deja termine ce flow avec succes.

Le flow est le suivant :

```text
Frontend avec un token valide
  -> GET /api/Core/authenticateUser
  -> Controller -> Dispatcher -> Handler -> AuthenticateUserService
  -> validation de l'organisation et du membre
  -> lecture de l'heure UTC depuis TimeProvider
  -> creation ou mise a jour du membre en base
  -> enregistrement reussi de LastSuccessfulAuthenticationAt
  <- 200 OK avec la session AssistantCore
```

La date est enregistree seulement apres la validation :

- du token;
- du tenant et du droit d'acceder a AssistantCore;
- de l'organisation active;
- du membre actif.

Pour un nouveau membre, la date est enregistree dans la meme operation que sa
creation. Pour un membre existant, la nouvelle date est enregistree avant le
retour `200 OK`.

Une reponse `401`, une reponse `403`, une annulation, une erreur technique ou
une erreur d'enregistrement en base ne modifie pas la date. Si l'enregistrement
echoue, AssistantCore ne doit pas retourner la session comme si tout avait
reussi.

Deux appels rapproches ou simultanes ne doivent jamais remplacer une date
recente par une date plus ancienne.

A la fin d'un appel reussi :

```text
Membre: actif et autorise
LastSuccessfulAuthenticationAt: date UTC du dernier authenticateUser reussi
Reponse frontend: session AssistantCore complete
```

---

### 11. Retourner la reponse au frontend

Si toutes les etapes se passent bien :
- retourner `200 OK`
- envoyer la reponse complete

Le frontend peut alors :
- afficher le nom de l'utilisateur
- savoir quelle entreprise est active
- savoir si l'utilisateur est `Admin` ou `User`

---

## Resume tres simple

`authenticateUser` fait ce travail :
1. verifier la session
2. lire l'identite externe
3. retrouver l'entreprise
4. verifier qu'elle est active
5. retrouver l'utilisateur interne
6. le creer automatiquement s'il n'existe pas
7. verifier qu'il est actif
8. deriver son role effectif depuis ses app roles Entra, sans synchroniser la base
9. construire la reponse
10. retourner le contexte complet au frontend

---

## Erreurs a prevoir

### 401 Unauthorized

A retourner si :
- l'utilisateur n'est pas connecte
- la session est invalide
- la session est expiree

### 403 Forbidden

A retourner si :
- l'entreprise n'existe pas dans la plateforme
- l'entreprise est suspendue
- l'utilisateur est suspendu
- l'utilisateur ne peut pas etre utilise localement
- la configuration Microsoft 365 n'est pas terminee et l'utilisateur n'a pas `TenantAdmin` (code metier `tenant_admin_required`)

### 500 Internal Server Error

A retourner si une erreur technique empeche la construction de la session.

---

## Regles metier fixes pour cet endpoint

- le nom fonctionnel de l'endpoint est `authenticateUser`
- l'utilisateur doit deja etre authentifie avant l'appel
- le compte utilisateur est cree automatiquement s'il n'existe pas
- le role effectif est derive des app roles Entra (`AssistantCore.Access` obligatoire, `TenantAdmin` optionnel) et non code en dur
- le role indicatif en base n'est pas utilise pour autoriser et n'est pas resynchronise a chaque authentification
- seul un app role defini sur l'App Registration d'AssistantCore compte pour cette derivation, jamais un role natif Microsoft comme `Global Administrator`
- il existe seulement deux roles : `Admin` et `User`
- le frontend ne choisit jamais librement l'organisation
- la reponse ne doit contenir aucune donnee sensible
- toutes les donnees retournees doivent appartenir a l'organisation courante

---

## References techniques

- [Autorisation des API avec Microsoft.Identity.Web](https://learn.microsoft.com/en-us/entra/msidweb/authentication/authorization)
- [Verifier les scopes et roles d'une API protegee](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-protected-web-api-verification-scope-app-roles)
- [Restreindre une application Entra a des utilisateurs affectes](https://learn.microsoft.com/en-us/entra/identity-platform/howto-restrict-your-app-to-a-set-of-users)
- [Ajouter des app roles dans Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity-platform/howto-add-app-roles-in-apps)
- [Claims des access tokens Microsoft Entra](https://learn.microsoft.com/en-us/entra/identity-platform/access-token-claims-reference)
