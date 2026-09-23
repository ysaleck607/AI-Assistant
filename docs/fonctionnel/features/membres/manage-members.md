# Gerer les membres de l'organisation

## Table des matieres

- [But](#but)
- [Regles communes](#regles-communes)
- [Afficher les membres](#endpoint-1---afficher-les-membres)
- [Etapes de GetMembers](#members-list-flow)
- [Modifier un role](#endpoint-2---modifier-le-role)
- [Etapes de UpdateMemberRole](#members-role-flow)
- [Erreurs](#erreurs-a-prevoir)
- [Resume](#resume)

## But

Cette fonctionnalite permet a un membre dont le jeton contient `TenantAdmin` :

- de voir les utilisateurs de son organisation
- de modifier l'etiquette indicative `Admin` ou `User` conservee sur leur fiche

Ces actions utilisent deux endpoints :

- `GET /api/members` affiche les membres
- `PATCH /api/members/{memberId}/role` modifie le role d'un membre

Un endpoint `GET` ne doit jamais modifier une donnee. Le changement du role
indicatif utilise donc un endpoint `PATCH` separe. Cette modification n'accorde
et ne retire aucun droit : les droits d'administration viennent exclusivement
du claim `TenantAdmin` du jeton courant.

## Regles communes

- l'utilisateur doit etre connecte
- l'organisation vient de l'identite de l'utilisateur connecte
- le frontend ne choisit jamais l'organisation
- seul un membre actif dont le jeton contient `AssistantCore.Access` et `TenantAdmin` peut utiliser ces endpoints
- un `TenantAdmin` ne peut consulter ou modifier que les membres de son organisation
- les seuls roles possibles sont `Admin` et `User`
- ces roles en base sont uniquement indicatifs
- les autorisations viennent des app roles du jeton, jamais du role en base

---

## Endpoint 1 - Afficher les membres

### Route

```http
GET /api/members
```

Le frontend n'envoie aucun body et aucun `organizationId`.

### Exemple de reponse

```json
{
  "members": [
    {
      "id": "a7c2d5b1-9b5d-4a8d-8b9f-2c4d6e8f2001",
      "displayName": "Marc Tremblay",
      "email": "marc.tremblay@metalpro.com",
      "role": "Admin",
      "status": "Active"
    },
    {
      "id": "b8d3e6c2-8c6e-5b9e-9c1a-3d5e7f9a3002",
      "displayName": "Sophie Gagnon",
      "email": "sophie.gagnon@metalpro.com",
      "role": "User",
      "status": "Active"
    }
  ]
}
```

<a id="members-list-flow"></a>
### Etapes de construction

#### 1. Verifier l'authentification

Concretement :

- verifier que le token est present et valide
- lire `externalUserId` et `externalTenantId` dans l'identite authentifiee

Si l'utilisateur n'est pas authentifie, retourner `401 Unauthorized`.

#### 2. Retrouver l'organisation

Concretement :

- chercher l'organisation interne avec `externalTenantId`
- verifier que son statut est `Active`

Si l'organisation n'existe pas ou n'est pas active, retourner `403 Forbidden`.

#### 3. Retrouver l'utilisateur connecte

Concretement :

- chercher le membre avec `externalUserId`
- limiter la recherche a l'organisation courante
- verifier que son statut est `Active`

Si le membre n'existe pas ou n'est pas actif, retourner `403 Forbidden`.

#### 4. Verifier le droit d'administration

Concretement :

- verifier que les app roles du jeton contiennent `AssistantCore.Access`
- continuer seulement si elles contiennent aussi `TenantAdmin`

Le frontend et le role indicatif de la fiche membre ne sont pas la source de verite.

Si `TenantAdmin` est absent, retourner `403 Forbidden`.

#### 5. Charger les membres

Concretement :

- chercher les membres avec l'identifiant interne de l'organisation courante
- ne jamais charger les membres d'une autre organisation
- inclure les membres actifs et inactifs pour donner une vue complete au `TenantAdmin`
- trier la liste par nom, puis par email

Pour chaque membre, retourner seulement :

- `id`
- `displayName`
- `email`
- `role`
- `status`

Ne pas retourner `externalUserId`, un token ou une donnee sensible.

#### 6. Retourner la reponse

Si le traitement reussit, retourner `200 OK` avec le tableau `members`.

Si aucun membre n'est trouve, retourner :

```json
{
  "members": []
}
```

---

## Endpoint 2 - Modifier le role

### Route

```http
PATCH /api/members/{memberId}/role
```

`memberId` est l'identifiant interne du membre a modifier.

### Indiquer Admin sur la fiche

```json
{
  "role": "Admin"
}
```

### Indiquer User sur la fiche

```json
{
  "role": "User"
}
```

### Exemple de reponse

```json
{
  "id": "b8d3e6c2-8c6e-5b9e-9c1a-3d5e7f9a3002",
  "displayName": "Sophie Gagnon",
  "email": "sophie.gagnon@metalpro.com",
  "role": "Admin",
  "status": "Active"
}
```

<a id="members-role-flow"></a>
### Etapes de construction

#### 1. Verifier l'administrateur connecte

Effectuer les memes controles que pour la liste :

- verifier l'authentification
- retrouver l'organisation active
- retrouver le membre connecte et actif
- verifier que son jeton contient `AssistantCore.Access` et `TenantAdmin`

Le role conserve sur sa fiche membre est indicatif et ne doit jamais autoriser
cette action.

Si un controle echoue, retourner `401` ou `403` sans modifier la base.

#### 2. Valider la demande

Concretement :

- verifier que `memberId` est un identifiant valide
- verifier que le body contient `role`
- accepter seulement `Admin` ou `User`

Refuser notamment `Manager`, `SuperAdmin`, une chaine vide ou un role absent avec `400 Bad Request`.

#### 3. Retrouver le membre cible

Concretement :

- chercher le membre avec `memberId`
- limiter obligatoirement la recherche a l'organisation courante
- verifier que le membre est actif

Si le membre n'existe pas dans cette organisation, retourner `404 Not Found`. Ne pas indiquer s'il existe dans une autre organisation.

Si le membre est inactif, retourner `400 Bad Request`.

#### 4. Empecher la modification de son propre role

Concretement :

- comparer l'identifiant du membre connecte avec `memberId`
- si les identifiants sont identiques, retourner `400 Bad Request`

Cette regle evite une modification accidentelle de sa propre fiche. Elle ne
protege pas son acces administratif, qui depend de `TenantAdmin` dans Entra.

#### 5. Enregistrer le nouveau role

Concretement :

- remplacer le role actuel par `Admin` ou `User`
- enregistrer la modification dans la base interne
- ne modifier ni le nom, ni l'email, ni le statut

Si le membre possede deja le role demande, ne pas retourner d'erreur. Conserver le role et continuer.

#### 6. Retourner le membre modifie

Retourner `200 OK` avec les informations actuelles du membre et son nouveau role.

---

## Erreurs a prevoir

### 400 Bad Request

- l'identifiant du membre est invalide
- le body ou le role est invalide
- le membre cible est inactif
- l'administrateur essaie de modifier son propre role indicatif

### 401 Unauthorized

- l'utilisateur n'est pas connecte
- le token est invalide ou expire

### 403 Forbidden

- l'organisation n'existe pas ou n'est pas active
- le membre connecte n'existe pas ou n'est pas actif
- le jeton du membre connecte ne contient pas `TenantAdmin`

### 404 Not Found

- le membre cible n'existe pas dans l'organisation courante

### 500 Internal Server Error

- une erreur technique inattendue empeche la lecture ou la modification

---

## Resume

`GET /api/members` verifie les droits `TenantAdmin` du membre connecte et retourne uniquement les membres de son organisation.

`PATCH /api/members/{memberId}/role` verifie le meme contexte, valide le membre
cible et enregistre son role indicatif `Admin` ou `User`. Cette valeur ne change
pas ses autorisations.

La désactivation et la réactivation utilisent un contrat séparé décrit dans
[Gérer le statut d'un membre](manage-member-status.md).
