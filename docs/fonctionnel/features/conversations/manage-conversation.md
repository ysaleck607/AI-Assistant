# Gérer le cycle de vie d'une conversation

## Table des matières

- [But](#conversation-management-purpose)
- [Renommer ou archiver](#conversation-management-patch)
- [Supprimer](#conversation-management-delete)
- [Purge physique](#conversation-management-purge)
- [Exemples de contrats](#conversation-management-examples)
- [Accès](#conversation-management-access)
- [Règles](#conversation-management-rules)
- [Traitement](#conversation-management-flow)
- [Erreurs](#conversation-management-errors)
- [Critères d'acceptation](#conversation-management-acceptance)

<a id="conversation-management-purpose"></a>
## But

Permettre au propriétaire de renommer, archiver, restaurer ou supprimer une
conversation sans exposer les conversations des autres membres.

<a id="conversation-management-patch"></a>
## Renommer ou archiver

```http
PATCH /api/conversations/{conversationId}
```

```json
{ "title": "Politique de télétravail", "status": "Archived" }
```

Chaque champ est optionnel, mais au moins un doit être fourni. `status` accepte
`Active` ou `Archived`. Un titre est nettoyé, non vide et limité par une
configuration backend.

La version connue du client voyage dans l'en-tête HTTP `If-Match`, pas dans le
corps : le corps ne contient que des champs modifiables, et tous y sont
optionnels.

```http
PATCH /api/conversations/conversation-123
If-Match: "7"
```

L'en-tête est optionnel. Absent, aucune vérification de concurrence n'est
demandée et la dernière écriture gagne. Présent mais illisible, la demande est
refusée avec `400` plutôt que d'écraser silencieusement une version plus
récente.

La réponse `200 OK` retourne la conversation actualisée :

```json
{
  "id": "conversation-123",
  "title": "Politique de télétravail",
  "status": "Archived",
  "updatedAt": "2026-08-18T14:30:00Z",
  "version": 7
}
```

<a id="conversation-management-delete"></a>
## Supprimer

```http
DELETE /api/conversations/{conversationId}
```

La réponse est `204 No Content`. La première étape est une suppression logique
avec date UTC. La purge physique est asynchrone et suit la politique de rétention.

<a id="conversation-management-purge"></a>
## Purge physique

La suppression logique dépose une demande de purge portant la date à partir de
laquelle elle devient éligible, calculée depuis la durée de récupération
configurée. Un worker séparé des contrôleurs HTTP la traite ensuite : une purge
peut durer et ne doit pas dépendre d'une requête utilisateur.

### Étapes

La purge s'exécute en quatre étapes, dans cet ordre :

| Étape | Effet |
| --- | --- |
| `DeleteMessageSources` | Efface les sources et avertissements des messages |
| `DeleteMessages` | Efface les messages |
| `DeleteConversation` | Efface la conversation |
| `Verify` | Constate qu'il ne reste plus aucune ligne |

Chaque étape réussie est enregistrée avant de passer à la suivante. Un arrêt
brutal reprend à la dernière étape confirmée au lieu de tout recommencer, et
chaque étape reste idempotente : une reprise au milieu d'une étape interrompue
ne produit ni doublon ni erreur.

Aucune étape ne vise Azure AI Search : l'index ne contient que du contenu
Microsoft 365, jamais de conversation ni de message.

### Reprise et échecs

Une demande réclamée porte un bail. Passé son expiration, elle redevient
disponible : un worker disparu en cours de route ne bloque jamais une purge.

Un échec est d'abord temporaire et reprogrammé avec un délai qui double à chaque
tentative, afin qu'une base indisponible ne soit pas harcelée. Au-delà du nombre
maximal de tentatives, il devient permanent : la demande cesse d'être rejouée et
reste visible pour alerte.

### Preuve

`Completed` n'est enregistré qu'après la vérification. Marquer une purge terminée
sans vérifier laisserait croire qu'une donnée a disparu alors qu'elle subsiste.

La demande de purge survit à la conversation qu'elle vient d'effacer et conserve
le nombre de messages et de sources supprimés, le nombre de tentatives et la date
de fin. **Elle ne conserve aucun contenu** : ni titre, ni message, ni source,
qui sont précisément ce que la purge vient de supprimer.

<a id="conversation-management-examples"></a>
## Exemples de contrats

### Renommer seulement

```http
PATCH /api/conversations/conversation-123
Content-Type: application/json
```

```json
{ "title": "Budget marketing 2027" }
```

Le statut reste inchangé. Après normalisation des espaces, un titre identique
est un no-op : aucune nouvelle version et aucun nouvel audit.

### Archiver puis restaurer

```json
{ "status": "Archived" }
```

Une conversation archivée quitte la liste des conversations récentes, mais
reste consultable. Le frontend la retrouve avec
[`GET /api/conversations?status=Archived`](list-conversations.md#conversations-status-filter),
ce qui lui permet d'afficher une section Archives et de proposer la
restauration. Son historique reste lisible, mais `POST /api/messages` refuse
tout nouveau message avec le code stable `conversation_archived`.

Sans ce filtre, une conversation archivée deviendrait introuvable et la règle
de restauration ci-dessous serait inapplicable. Pour la restaurer :

```json
{ "status": "Active" }
```

### Suppression logique

Après `DELETE`, les lectures ordinaires se comportent comme si la conversation
n’existait plus. Répéter le même `DELETE` retourne encore `204` sans révéler
son ancien titre ni créer un deuxième travail de purge.

### Conflit concurrent

Deux onglets utilisent la version 7. Le premier renommage produit la version 8.
Le second reçoit :

```json
{
  "code": "conversation_version_conflict",
  "message": "La conversation a été modifiée dans une autre session."
}
```

Le frontend recharge alors la conversation au lieu d’écraser la version 8.

<a id="conversation-management-access"></a>
## Accès

Seul le propriétaire actif dans l'organisation courante peut agir. Un Admin
n'obtient aucun accès automatique aux conversations d'un collègue.

<a id="conversation-management-rules"></a>
## Règles

- Une conversation archivée reste consultable mais refuse un nouveau message.
- Une conversation peut être restaurée vers `Active` avant sa suppression.
- Une conversation supprimée disparaît des listes et lectures ordinaires.
- Une répétition de DELETE reste idempotente sans révéler l'existence passée.
- Chaque modification réelle est auditée.

<a id="conversation-management-flow"></a>
## Traitement

1. Construire le contexte membre et organisation.
2. Charger avec `conversationId + organizationId + ownerMemberId`.
3. Valider le patch ou la suppression.
4. Appliquer une protection de concurrence.
5. Enregistrer la conversation et l'audit dans une unité cohérente.
6. Pour DELETE, publier ou rendre disponible le travail de purge après commit.

<a id="conversation-management-errors"></a>
## Erreurs

- `400` : patch vide, titre ou statut invalide.
- `401` : token invalide.
- `403` : membre ou organisation inactive.
- `404` : conversation absente ou étrangère.
- `409` : modification concurrente.

<a id="conversation-management-acceptance"></a>
## Critères d'acceptation

- Le propriétaire peut renommer, archiver, restaurer et supprimer.
- Une conversation archivée refuse `POST /api/messages`.
- Une suppression disparaît immédiatement des lectures normales.
- Les données physiques sont purgées selon la rétention.
- Les actions sont isolées, idempotentes, auditées et testées.
