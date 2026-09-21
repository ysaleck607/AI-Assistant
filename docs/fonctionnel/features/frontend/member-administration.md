# Administrer les membres dans Angular

## Table des matières

- [But](#member-admin-purpose)
- [Route Angular](#member-admin-route)
- [Données utilisées](#member-admin-api)
- [Écran](#member-admin-screen)
- [Actions](#member-admin-actions)
- [Exemple de parcours](#member-admin-example)
- [Erreurs et accessibilité](#member-admin-errors)
- [Architecture](#member-admin-architecture)
- [Critères d'acceptation](#member-admin-acceptance)

<a id="member-admin-purpose"></a>
## But

Donner à un `TenantAdmin` une interface séparée du chat pour consulter les
membres, changer leur rôle indicatif et activer ou désactiver leur accès.

<a id="member-admin-route"></a>
## Route Angular

```text
/admin/members
```

Un guard masque la route lorsque le rôle effectif de la session n'est pas
`Admin`. Ce rôle effectif vient de `TenantAdmin` dans le jeton. Le backend
refait toujours le contrôle; masquer une route n'est pas une autorisation.

<a id="member-admin-api"></a>
## Données utilisées

```http
GET /api/members
PATCH /api/members/{memberId}/role
PATCH /api/members/{memberId}/status
```

<a id="member-admin-screen"></a>
## Écran

Le tableau affiche nom, courriel, rôle et statut. Il possède les états de
chargement, liste vide et erreur. Le membre connecté est clairement identifié.

<a id="member-admin-actions"></a>
## Actions

1. Le `TenantAdmin` choisit un rôle indicatif ou un statut.
2. Une confirmation explique l'effet d'une désactivation.
3. Le bouton concerné est bloqué pendant l'appel.
4. La ligne est remplacée par la réponse du backend.
5. Une erreur restaure l'affichage précédent et reste lisible.

Les actions interdites sur soi-même sont désactivées visuellement, sans retirer
la validation backend.

<a id="member-admin-example"></a>
## Exemple de parcours

L’écran affiche :

| Membre | Rôle | Statut | Actions |
| --- | --- | --- | --- |
| Alice Tremblay (vous) | Admin | Actif | actions sur soi désactivées |
| Bob Martin | User | Actif | changer le rôle, désactiver |

Lorsque Alice désactive Bob :

1. une boîte de confirmation affiche `Bob ne pourra plus accéder à AssistantCore`;
2. seul le bouton de la ligne Bob est désactivé;
3. Angular appelle `PATCH /api/members/member-bob/status` avec
   `{"status":"Inactive"}`;
4. après `200 OK`, le store remplace la ligne avec la réponse backend;
5. un message accessible confirme `Bob Martin a été désactivé`.

Une erreur ne doit jamais laisser le tableau montrer une modification refusée.

Exemple d’état frontend exposé en lecture seule :

```typescript
{
  members: MemberSummary[];
  loading: boolean;
  pendingMemberIds: ReadonlySet<string>;
  error: MemberAdministrationError | null;
}
```

<a id="member-admin-errors"></a>
## Erreurs et accessibilité

- `401` redemande une session.
- `403` ferme la route et affiche l'accès refusé.
- `404` retire la ligne devenue inaccessible.
- `409` signale une modification concurrente du membre.
- Les contrôles ont des labels, un focus visible et restent utilisables au clavier.

<a id="member-admin-architecture"></a>
## Architecture

Utiliser des composants standalone, Reactive Forms, des services `HttpClient`
et un service d'état exposant des signals en lecture seule. Aucun appel HTTP
n'est placé dans un composant de présentation.

<a id="member-admin-acceptance"></a>
## Critères d'acceptation

- Seul un membre dont le jeton contient `TenantAdmin` accède à la route.
- Le rôle affiché dans le tableau est indicatif et ne change pas les autorisations.
- La liste et les actions utilisent les contrats backend documentés.
- Les refus ne laissent jamais un faux rôle ou statut à l'écran.
- Le parcours fonctionne sur mobile et au clavier.
- Les services, composants et erreurs sont testés.
