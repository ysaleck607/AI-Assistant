# Charger les messages d'une conversation

## Table des matieres

- [But](#conversation-messages-purpose)
- [Route](#conversation-messages-route)
- [Acces](#conversation-messages-access)
- [Donnees du frontend](#conversation-messages-request)
- [Pagination](#conversation-messages-pagination)
- [Exemple de reponse](#conversation-messages-response)
- [Champs retournes](#conversation-messages-fields)
- [Etapes de traitement](#conversation-messages-flow)
- [Securite](#conversation-messages-security)
- [Architecture](#conversation-messages-architecture)
- [Erreurs](#conversation-messages-errors)
- [Hors perimetre](#conversation-messages-out-of-scope)
- [Criteres d'acceptation](#conversation-messages-acceptance)
- [Relation avec les autres endpoints](#conversation-messages-relations)

<a id="conversation-messages-purpose"></a>
## But

`GET /api/conversations/{conversationId}/messages` permet a un utilisateur
authentifie de charger l'historique d'une conversation qui lui appartient.

La reponse contient les messages utilisateur et Assistant, leurs etats de
traitement et les sources enregistrees avec les reponses de l'Assistant.

Cet endpoint est uniquement un endpoint de lecture. Il ne cree aucun message,
ne relance aucun traitement et ne modifie pas la conversation.

<a id="conversation-messages-route"></a>
## Route

```http
GET /api/conversations/{conversationId}/messages
```

Exemple avec pagination :

```http
GET /api/conversations/8d7df699-13f8-4c85-871f-115d049bc697/messages?limit=50&cursor=eyJjcmVhdGVkQXQiOi...
```

<a id="conversation-messages-access"></a>
## Qui peut utiliser cet endpoint

Un membre authentifie peut l'utiliser si :

- son organisation existe et est active
- son compte interne existe et est actif
- son jeton contient `AssistantCore.Access`
- la configuration Microsoft 365 est terminee, ou son jeton contient aussi `TenantAdmin`
- la conversation appartient a ce membre dans l'organisation courante

Tous les membres voient uniquement leurs propres conversations.
Un `TenantAdmin` ne peut pas lire automatiquement les conversations des
autres membres.

Une conversation archivee reste consultable par son proprietaire. Son
archivage peut empecher l'ajout de nouveaux messages, mais ne supprime pas son
historique.

<a id="conversation-messages-request"></a>
## Donnees envoyees par le frontend

L'endpoint ne recoit aucun body.

### `conversationId`

`conversationId` est obligatoire dans la route.

- il doit etre un identifiant valide
- il ne peut pas etre vide
- il doit identifier une conversation appartenant au membre connecte

Le frontend ne doit jamais envoyer :

- l'identifiant de l'organisation
- l'identifiant du membre proprietaire
- un identifiant externe provenant du fournisseur d'identite

Ces valeurs viennent du contexte d'authentification du backend.

<a id="conversation-messages-pagination"></a>
## Pagination

### `limit`

`limit` est optionnel.

- valeur par defaut : `50`
- valeur maximale : `100`
- une valeur inferieure a `1` ou superieure a `100` retourne `400`

### `cursor`

`cursor` est optionnel.

- il est retourne par la page precedente
- il est opaque pour le frontend
- le frontend ne doit pas le construire ou le modifier
- il permet de charger les messages plus anciens
- un curseur invalide retourne `400`
- un curseur cree pour une autre conversation retourne `400`

Le curseur doit permettre une pagination stable selon :

1. `createdAt`
2. `id` pour departager deux messages crees a la meme date

Le backend charge d'abord les messages les plus recents. Les messages contenus
dans chaque page sont toutefois retournes dans l'ordre chronologique, du plus
ancien au plus recent.

Le backend ne doit pas utiliser une pagination basee uniquement sur un numero
de page.

<a id="conversation-messages-response"></a>
## Exemple de reponse

```json
{
  "conversationId": "8d7df699-13f8-4c85-871f-115d049bc697",
  "messages": [
    {
      "id": "d60f7d9f-3e17-4c58-b163-3d4097cf985f",
      "role": "User",
      "content": "Quelle est notre politique de teletravail ?",
      "processingStatus": "Completed",
      "model": null,
      "createdAt": "2026-08-06T20:15:00Z",
      "updatedAt": "2026-08-06T20:15:08Z",
      "sources": []
    },
    {
      "id": "771e2282-f6ef-4876-a209-3200220d668f",
      "role": "Assistant",
      "content": "La politique permet jusqu'a deux jours de teletravail.",
      "processingStatus": "Completed",
      "model": "gpt",
      "createdAt": "2026-08-06T20:15:08Z",
      "updatedAt": "2026-08-06T20:15:08Z",
      "sources": [
        {
          "type": "SharePoint",
          "title": "Politique de teletravail",
          "url": "https://example.sharepoint.com/politique",
          "reference": "document-123",
          "sourceDate": "2026-05-01T00:00:00Z"
        }
      ]
    }
  ],
  "nextCursor": "eyJjcmVhdGVkQXQiOi...",
  "hasMore": true
}
```

Lorsqu'une conversation ne contient aucun message :

```json
{
  "conversationId": "8d7df699-13f8-4c85-871f-115d049bc697",
  "messages": [],
  "nextCursor": null,
  "hasMore": false
}
```

<a id="conversation-messages-fields"></a>
## Regles des champs retournes

### `role`

Les valeurs possibles sont :

- `User`
- `Assistant`

### `processingStatus`

L'endpoint retourne l'etat persiste du message :

- `Pending`
- `InProgress`
- `Completed`
- `Failed`
- `Cancelled`

Un message utilisateur en echec ou encore en traitement reste visible. Le GET
ne tente pas de reprendre ou de corriger son traitement.

### `model`

`model` indique la famille de modele utilisee pour une reponse de l'Assistant.
Il est normalement `null` pour un message utilisateur.

### `sources`

Les sources sont les references persistees lors de la production de la
reponse. Un message sans source retourne un tableau vide.

Le GET :

- ne relance aucun connecteur
- ne relance aucune recherche
- ne recharge pas le contenu complet des documents
- ne retourne aucun secret ou identifiant technique de connexion

Le controle d'acces au contenu cible reste applique par le systeme externe
lorsque l'utilisateur ouvre une URL.

### Dates

Toutes les dates sont retournees en UTC.

- `createdAt` indique la creation du message
- `updatedAt` indique sa derniere modification, notamment un changement
  d'etat de traitement
- `sourceDate` indique la date fonctionnelle de la source lorsqu'elle existe

<a id="conversation-messages-flow"></a>
## Etapes de traitement

### 1. Verifier l'authentification

- verifier le JWT
- lire les identifiants externes du tenant et de l'utilisateur
- retourner `401` si l'identite est absente ou invalide

### 2. Retrouver le contexte interne

- retrouver l'organisation
- verifier qu'elle est active
- retrouver le membre selon le flux d'authentification existant
- verifier que le membre est actif

Une organisation ou un membre interdit retourne `403`.

### 3. Valider la requete

- verifier `conversationId`
- appliquer la limite par defaut
- refuser une limite hors des valeurs autorisees
- decoder et verifier le curseur lorsqu'il est present
- verifier que le curseur correspond a la conversation demandee

Une pagination invalide retourne `400` avant le chargement des messages.

### 4. Verifier la conversation

La conversation doit etre recherchee avec :

- son identifiant
- l'identifiant interne de l'organisation
- l'identifiant interne du membre proprietaire

Une conversation inexistante ou appartenant a un autre utilisateur ou a une
autre organisation retourne `404`.

La reponse ne doit pas permettre de distinguer ces situations.

### 5. Charger les messages

La lecture doit :

- appliquer les memes filtres de securite que la conversation
- utiliser une projection limitee aux champs de la reponse
- charger au maximum `limit + 1` messages
- utiliser `createdAt` et `id` pour garantir un ordre stable
- charger uniquement les sources associees aux messages de la page

L'historique complet ne doit jamais etre charge en memoire pour construire une
page.

### 6. Construire la reponse

Les messages retenus sont retournes dans l'ordre chronologique.

Si des messages plus anciens existent :

- construire le curseur a partir du message le plus ancien retourne
- retourner `hasMore = true`

Sinon :

- retourner `nextCursor = null`
- retourner `hasMore = false`

<a id="conversation-messages-security"></a>
## Securite

- L'organisation et le membre viennent toujours du contexte backend.
- La conversation doit appartenir au membre connecte.
- Le role Admin ne contourne pas la regle de propriete.
- Une conversation inaccessible retourne `404` sans confirmer son existence.
- Le curseur ne remplace jamais les filtres de propriete.
- Les messages et les sources d'une autre conversation ne sont jamais
  retournes.
- Aucun token, secret ou contenu complet de source n'est retourne.
- Le partage de conversations est hors perimetre de cette version.

<a id="conversation-messages-architecture"></a>
## Architecture attendue

Le traitement respecte le flux :

```text
Controller
  -> IDispatcher
  -> CommandHandler
  -> Application Service
  -> Query de lecture
```

- Le Controller recoit les parametres et appelle uniquement le dispatcher.
- Le handler orchestre uniquement l'appel au service applicatif.
- Le service applicatif gere le contexte authentifie et les regles de lecture.
- La query applique les filtres de securite, la pagination et la projection.
- Le Controller et le handler ne contiennent aucune requete EF Core.

La lecture doit utiliser `AsNoTracking` et une projection. Elle ne doit pas
charger l'agregat complet de la conversation ni tous ses messages.

<a id="conversation-messages-errors"></a>
## Erreurs a prevoir

### `400 Bad Request`

- `conversationId` est vide ou son format est invalide
- `limit` est inferieur a `1` ou superieur a `100`
- `cursor` est invalide
- `cursor` ne correspond pas a la conversation demandee

### `401 Unauthorized`

- le token est absent, invalide ou expire
- un claim obligatoire est absent

### `403 Forbidden`

- l'organisation est absente ou inactive
- le membre est inactif ou interdit

### `404 Not Found`

- la conversation n'existe pas pour le membre dans l'organisation courante

### `500 Internal Server Error`

- une erreur technique empeche la lecture

<a id="conversation-messages-out-of-scope"></a>
## Hors perimetre

Cette premiere version ne couvre pas :

- l'envoi d'un nouveau message
- le streaming d'une reponse en cours
- la relance d'un message en echec
- la modification ou la suppression d'un message
- la recherche dans l'historique
- le partage d'une conversation
- le rechargement du contenu complet des sources

<a id="conversation-messages-acceptance"></a>
## Criteres d'acceptation

- Un utilisateur charge uniquement les messages de ses conversations.
- Un administrateur ne peut pas lire la conversation d'un autre membre.
- Une conversation inaccessible retourne `404`.
- Une conversation archivee reste consultable par son proprietaire.
- La premiere page contient les messages les plus recents.
- Chaque page est retournee dans l'ordre chronologique.
- La pagination est stable lorsque plusieurs messages ont la meme date.
- Un curseur invalide retourne `400`.
- Une conversation sans message retourne `200` avec un tableau vide.
- Les messages non termines ou en echec restent visibles avec leur etat.
- Les sources sont limitees aux messages de la page.
- Le GET ne relance aucun traitement et ne modifie aucune donnee.
- La requete ne charge pas tout l'historique en memoire.
- Les tests couvrent plusieurs membres, plusieurs organisations, les
  conversations archivees, tous les etats de traitement, les dates identiques
  et les curseurs invalides.

<a id="conversation-messages-relations"></a>
## Relation avec les autres endpoints

- `GET /api/conversations` liste les conversations actives du membre.
- `POST /api/messages` cree une conversation ou ajoute un nouveau message.
- `PATCH /api/conversations/{id}` permettra de renommer ou archiver une
  conversation.
- `DELETE /api/conversations/{id}` appliquera la politique de suppression et
  de retention.
