# Gérer le statut d'un membre

## Table des matières

- [But](#member-status-purpose)
- [Route](#member-status-route)
- [Accès](#member-status-access)
- [Contrat](#member-status-contract)
- [Exemples concrets](#member-status-examples)
- [Règles](#member-status-rules)
- [Traitement](#member-status-flow)
- [Erreurs](#member-status-errors)
- [Audit](#member-status-audit)
- [Critères d'acceptation](#member-status-acceptance)

<a id="member-status-purpose"></a>
## But

Permettre à un administrateur de bloquer immédiatement les accès applicatifs
d'un membre ou de réactiver son compte, sans modifier son identité ni son rôle.

<a id="member-status-route"></a>
## Route

```http
PATCH /api/members/{memberId}/status
```

<a id="member-status-access"></a>
## Accès

L'appelant doit être un membre interne actif dont le jeton contient
`AssistantCore.Access` et `TenantAdmin`. Le membre cible doit appartenir à la
même organisation. Le rôle indicatif en base n'autorise pas cet endpoint.

<a id="member-status-contract"></a>
## Contrat

```json
{ "status": "Inactive" }
```

Les seules valeurs acceptées sont `Active` et `Inactive`. La réponse `200`
retourne le même contrat membre que `PATCH /api/members/{id}/role`.

Exemple de réponse après désactivation :

```json
{
  "id": "member-bob",
  "displayName": "Bob Martin",
  "email": "bob@contoso.com",
  "role": "User",
  "status": "Inactive",
  "version": 4
}
```

Le champ `version` représente la version de concurrence retournée par le
backend. Le frontend remplace sa ligne avec cette réponse; il ne fabrique pas
localement un nouveau membre.

<a id="member-status-examples"></a>
## Exemples concrets

### Désactivation autorisée

Le jeton d'Alice contient `TenantAdmin`. Bob est un membre actif de la même organisation. Alice
envoie `{"status":"Inactive"}` pour Bob. Le backend :

1. retrouve Alice et son organisation depuis le JWT;
2. charge Bob avec `organizationId + memberId`;
3. enregistre `Inactive` avec contrôle de version;
4. écrit un audit `MemberStatusChanged` dans la même transaction;
5. retourne Bob avec son nouveau statut.

Au prochain appel protégé, Bob reçoit `403 Forbidden`, même si son token Entra
n’est pas encore expiré.

### Répétition idempotente

Si Bob est déjà `Inactive`, répéter la même requête retourne son état courant
sans modifier la version et sans créer un deuxième audit.

<a id="member-status-rules"></a>
## Règles

- Un administrateur ne peut pas modifier son propre statut.
- Réactiver un membre conserve son rôle précédent.
- Répéter la valeur déjà enregistrée réussit sans créer un deuxième audit.
- Un membre désactivé est refusé par tous les endpoints au prochain appel,
  même si son access token Microsoft reste valide.
- L'endpoint ne modifie pas l'affectation Entra `AssistantCore.Access`.
- L'endpoint ne peut pas déterminer les autres `TenantAdmin` depuis les rôles
  indicatifs en base et n'applique donc pas de règle de « dernier Admin ».

<a id="member-status-flow"></a>
## Traitement

1. Valider le JWT, l'organisation, le membre courant et son app role `TenantAdmin`.
2. Valider `memberId` et la valeur du statut.
3. Charger la cible avec l'organisation courante.
4. Refuser une modification de soi-même.
5. Enregistrer le statut avec protection contre les modifications concurrentes.
6. Ajouter l'entrée d'audit dans la même unité cohérente.
7. Retourner le membre actualisé.

Le flow respecte `Controller -> IDispatcher -> Handler -> Application Service -> Repository`.

<a id="member-status-errors"></a>
## Erreurs

- `400` : identifiant/statut invalide ou modification de soi-même.
- `401` : token absent ou invalide.
- `403` : claim `TenantAdmin` absent, membre ou organisation inactive.
- `404` : cible absente ou appartenant à une autre organisation.
- `409` : modification concurrente du membre cible.

<a id="member-status-audit"></a>
## Audit

Une modification réelle enregistre l'acteur, la cible, l'ancien statut, le
nouveau statut, la date UTC et le correlation ID, sans token ni claims complets.

<a id="member-status-acceptance"></a>
## Critères d'acceptation

- Un `TenantAdmin` peut désactiver et réactiver un autre membre de son organisation.
- Un membre désactivé est immédiatement refusé par les endpoints protégés.
- Le rôle du membre ne change pas.
- L'isolation des organisations et l'audit sont testés.
- `dotnet test Solution.sln` réussit.
