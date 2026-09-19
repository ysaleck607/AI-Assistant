# Limiter les requêtes et les orchestrations simultanées

## Table des matières

- [But](#rate-limit-purpose)
- [Version actuelle : première version single-instance](#rate-limit-v1)
- [Configuration et clés](#rate-limit-configuration)
- [Réponse](#rate-limit-response)
- [Ordre des contrôles](#rate-limit-order)
- [Orchestrations simultanées](#rate-limit-orchestrations)
- [Limites de la première version](#rate-limit-v1-limits)
- [Évolution Redis et multi-instance](#rate-limit-distributed)
- [Frontend](#rate-limit-frontend)
- [Critères d'acceptation de la première version](#rate-limit-acceptance)

<a id="rate-limit-purpose"></a>
## But

Protéger l'envoi de messages contre deux formes de surcharge : les rafales de
requêtes et un trop grand nombre d'orchestrations IA exécutées en même temps.
Après authentification, les limites reposent sur le membre et l'organisation
internes validés et non sur l'adresse IP.

La première version vise volontairement un déploiement avec une seule instance
de l'API. Les compteurs et les places d'orchestration sont donc conservés dans
la mémoire du processus, derrière des abstractions applicatives qui pourront
recevoir une implémentation Redis lorsque plusieurs instances seront
nécessaires.

<a id="rate-limit-v1"></a>
## Version actuelle : première version single-instance

La première version protège :

- `POST /api/messages`;
- `POST /api/messages/stream`.

Pour chaque nouvel envoi, deux règles de fréquence sont d'abord évaluées
ensemble :

1. un compteur propre au membre dans cette organisation;
2. un compteur partagé par toute l'organisation.

L'acquisition est atomique dans le store mémoire : les deux compteurs ne sont
incrémentés que si les deux limites autorisent la requête. Si la limite membre
ou la limite organisation est déjà atteinte, aucun des deux compteurs n'est
consommé par la tentative refusée.

Après ces contrôles, l'API tente d'acquérir une place d'orchestration pour
l'organisation. Une organisation ne peut exécuter qu'un nombre configurable
d'orchestrations IA en parallèle. La place est acquise avant l'enregistrement
de la question et avant tout appel à Foundry.

Pour le streaming, tous ces contrôles sont terminés avant le démarrage de la
réponse Server-Sent Events. Un refus peut donc encore retourner un véritable
statut HTTP `429` avec l'en-tête `Retry-After`.

<a id="rate-limit-configuration"></a>
## Configuration et clés

Valeurs initiales configurables :

```json
{
  "RateLimiting": {
    "MemberMessagesPerMinute": 10,
    "OrganizationMessagesPerMinute": 100,
    "OrganizationConcurrentOrchestrations": 5,
    "OrchestrationLeaseSeconds": 300
  }
}
```

Toutes ces valeurs doivent être strictement supérieures à zéro et sont validées
au démarrage de l'application.

Les compteurs de fréquence utilisent uniquement les identifiants internes
validés :

```text
rate:member:{organizationId}:{memberId}:messages
rate:organization:{organizationId}:messages
```

Alice et Bob possèdent donc chacun leur compteur membre, mais leurs envois
alimentent aussi le même compteur si les deux appartiennent à la même
organisation. Les places d'orchestration sont également isolées par
organisation.

<a id="rate-limit-response"></a>
## Réponse

Lorsqu'une limite de fréquence ou une limite d'orchestrations simultanées est
atteinte :

```http
429 Too Many Requests
Retry-After: 30
```

```json
{
  "code": "request_rate_limit_exceeded",
  "message": "Trop de demandes ont été envoyées.",
  "metadata": { "retryAfterSeconds": 30 }
}
```

Pour une limite de fréquence, `Retry-After` correspond au délai nécessaire pour
que les règles bloquantes puissent accepter une nouvelle requête. Pour une
limite d'orchestrations simultanées, il correspond au prochain bail qui doit
expirer si aucune orchestration ne se termine plus tôt.

`request_rate_limit_exceeded` reste distinct de
`organization_token_quota_exhausted` et de `ai_provider_rate_limited`. Le
frontend choisit donc son comportement avec `code`, jamais uniquement avec le
statut HTTP `429`.

<a id="rate-limit-order"></a>
## Ordre des contrôles

Pour `POST /api/messages` et `POST /api/messages/stream` :

1. ASP.NET valide l'authentification.
2. Le handler valide la commande.
3. Le service de contexte retrouve le membre et l'organisation internes.
4. Le rate limiting évalue atomiquement les règles membre et organisation.
5. Si une limite de fréquence est atteinte, la requête s'arrête avec `429`.
6. L'API tente ensuite d'acquérir une place d'orchestration pour l'organisation.
7. Si aucune place n'est disponible, la requête s'arrête avec `429` avant toute
   persistance et avant tout appel au modèle.
8. Si une place est acquise, le lifecycle enregistre la question et prépare le
   traitement.
9. L'agent exécute les appels externes et produit sa réponse.
10. La place est libérée dans un `finally`, que le traitement réussisse, échoue
    ou soit annulé.

Une requête refusée parce que toutes les places sont occupées a déjà passé le
contrôle de fréquence. Elle peut donc avoir consommé son compteur de fréquence,
mais elle ne crée aucun message et ne consomme aucun appel modèle.

<a id="rate-limit-orchestrations"></a>
## Orchestrations simultanées

Chaque place acquise reçoit un `leaseId` interne unique et une date d'expiration.
Le store mémoire protège l'acquisition et la libération avec une opération
synchronisée afin que des appels concurrents sur la même instance ne dépassent
pas la limite configurée.

La libération est idempotente : libérer deux fois le même lease ne retire jamais
une deuxième place. Lorsqu'une orchestration se termine normalement, échoue ou
est annulée, le handler libère sa place dans `finally`.

Si un traitement abandonne sa place sans exécuter ce `finally`, par exemple à
cause d'une défaillance interne inattendue du traitement dans le processus, le
lease expiré est retiré à la prochaine tentative d'acquisition. Une place ne
reste donc pas bloquée indéfiniment dans la mémoire d'une instance encore en
fonctionnement.

Les métriques exposent le nombre de places actives, les acquisitions refusées et
les leases expirés. Elles ne placent aucun identifiant de membre ou
d'organisation dans les labels.

<a id="rate-limit-v1-limits"></a>
## Limites de la première version

Cette première version assume explicitement une seule instance de l'API.

Elle ne fournit pas encore :

- de compteurs ou de places partagés entre plusieurs replicas;
- Redis ou un autre stockage distribué;
- de limite générale sur tous les endpoints de l'API;
- de stratégie `rate_limit_store_unavailable`, car les limites ne dépendent pas
  encore d'un service externe;
- de tests simulant plusieurs instances de l'API.

Un redémarrage du processus remet les compteurs à zéro et supprime tous les
leases mémoire. Cela libère les places plutôt que de les laisser bloquées, mais
cela signifie également que l'état de limitation n'est pas conservé entre deux
processus. Ce comportement est accepté pour la première version et ne doit pas
être conservé lorsque l'application passera à plusieurs instances.

<a id="rate-limit-distributed"></a>
## Évolution Redis et multi-instance

Lorsque l'API devra fonctionner avec plusieurs instances, les abstractions de
stockage devront recevoir des implémentations Redis sans déplacer la logique de
décision dans les controllers ou dans les handlers.

Cette évolution devra au minimum ajouter :

- une acquisition atomique des règles membre et organisation avec expiration;
- des compteurs partagés par toutes les instances;
- des places d'orchestration partagées entre les replicas;
- des leases Redis possédant un identifiant, une expiration et une libération
  idempotente;
- une stratégie explicite lorsque Redis est indisponible. Pour les endpoints de
  génération coûteux, aucun appel au modèle ne doit démarrer si le système ne
  peut pas vérifier la limite;
- une réponse contrôlée `503 rate_limit_store_unavailable` dans ce scénario;
- des tests de concurrence, d'expiration et de partage entre au moins deux
  instances simulées.

<a id="rate-limit-frontend"></a>
## Frontend

Angular distingue les trois causes de `429` :

- `request_rate_limit_exceeded` : la question refusée reste dans le compositeur,
  l'action d'envoi est bloquée pendant `retryAfterSeconds` et un compte à rebours
  est affiché. Aucun renvoi automatique n'est effectué; l'utilisateur choisit
  de réessayer;
- `organization_token_quota_exhausted` : l'envoi reste bloqué jusqu'au
  renouvellement du quota indiqué par `periodEndsAt`;
- `ai_provider_rate_limited` : l'utilisateur reçoit un message temporaire lui
  demandant de réessayer plus tard.

Pendant un blocage, le texte du compositeur reste lisible et modifiable. Seule
l'action d'envoi est empêchée lorsque cela est nécessaire.

<a id="rate-limit-acceptance"></a>
## Critères d'acceptation de la première version

- `POST /api/messages` et `POST /api/messages/stream` partagent les mêmes règles
  de fréquence et la même limite d'orchestrations par organisation.
- Les limites proviennent de la configuration et sont validées au démarrage.
- Deux membres d'une même organisation possèdent des compteurs membre distincts
  et partagent le compteur organisation.
- Une organisation ne peut pas dépasser le nombre de places d'orchestration
  configuré sur l'instance.
- Une double libération ne rend jamais une place supplémentaire disponible.
- Un lease abandonné peut expirer et sa place peut être récupérée.
- Succès, erreur et annulation libèrent le lease dans `finally`.
- Une requête refusée avant orchestration retourne `429`,
  `request_rate_limit_exceeded`, `Retry-After` et `retryAfterSeconds`.
- Aucun lifecycle de message ni appel à l'agent ne démarre lorsqu'aucune place
  n'est disponible.
- Angular distingue le rate limit applicatif, le quota d'organisation et le
  rate limit du fournisseur, conserve une question refusée et ne la renvoie pas
  automatiquement.
- Le code dépend d'abstractions permettant une implémentation Redis ultérieure.
