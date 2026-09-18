# Limiter les requêtes et les orchestrations simultanées

## Table des matières

- [But](#rate-limit-purpose)
- [Version actuelle : première version single-instance](#rate-limit-v1)
- [Configuration et clés](#rate-limit-configuration)
- [Réponse](#rate-limit-response)
- [Ordre des contrôles](#rate-limit-order)
- [Limites de la première version](#rate-limit-v1-limits)
- [Évolution Redis et multi-instance](#rate-limit-distributed)
- [Frontend](#rate-limit-frontend)
- [Critères d'acceptation de la première version](#rate-limit-acceptance)

<a id="rate-limit-purpose"></a>
## But

Protéger l'envoi de messages contre les rafales qui pourraient démarrer trop
d'appels IA en peu de temps. Après authentification, la limite repose sur le
membre et l'organisation internes validés et non sur l'adresse IP.

La première version vise volontairement un déploiement avec une seule instance
de l'API. Le code garde toutefois une abstraction de stockage afin de pouvoir
remplacer le compteur mémoire par Redis lorsque plusieurs instances seront
nécessaires.

<a id="rate-limit-v1"></a>
## Version actuelle : première version single-instance

La première version limite les endpoints suivants :

- `POST /api/messages`;
- `POST /api/messages/stream`.

Pour chaque nouvel envoi, deux règles sont évaluées ensemble :

1. un compteur propre au membre dans cette organisation;
2. un compteur partagé par toute l'organisation.

L'acquisition est atomique dans le store mémoire : les deux compteurs ne sont
incrémentés que si les deux limites autorisent la requête. Si la limite membre
ou la limite organisation est déjà atteinte, aucun des deux compteurs n'est
consommé par la tentative refusée.

Les compteurs utilisent une fenêtre fixe d'une minute et sont conservés dans la
mémoire du processus de l'API. Ils sont donc cohérents tant que l'application
tourne sur une seule instance. Les fenêtres expirées sont supprimées
opportunément lors des nouvelles acquisitions afin que les anciennes clés ne
restent pas indéfiniment en mémoire.

Le contrôle est effectué après la résolution du membre et de l'organisation,
mais avant l'enregistrement de la question et avant tout appel à Foundry ou à un
autre fournisseur IA. Une requête refusée ne consomme donc pas un appel modèle.

Pour le streaming, le contrôle est terminé avant le démarrage de la réponse
Server-Sent Events. Cela permet encore de retourner un véritable statut HTTP
`429` avec l'en-tête `Retry-After`.

<a id="rate-limit-configuration"></a>
## Configuration et clés

Valeurs initiales configurables :

```json
{
  "RateLimiting": {
    "MemberMessagesPerMinute": 10,
    "OrganizationMessagesPerMinute": 100
  }
}
```

Les valeurs doivent être strictement supérieures à zéro et sont validées au
démarrage de l'application.

Les clés utilisent uniquement les identifiants internes validés :

```text
rate:member:{organizationId}:{memberId}:messages
rate:organization:{organizationId}:messages
```

Alice et Bob possèdent donc chacun leur compteur membre, mais leurs envois
alimentent aussi le même compteur si les deux appartiennent à la même
organisation.

L'adresse IP peut un jour servir à protéger une surface publique avant
authentification, mais elle ne remplace jamais les clés membre et organisation
après authentification.

<a id="rate-limit-response"></a>
## Réponse

Lorsqu'une limite est atteinte :

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

Si plusieurs règles sont simultanément bloquantes, `Retry-After` utilise le
délai le plus long afin que le client ne réessaie pas avant que toutes les
règles nécessaires puissent accepter une nouvelle requête.

`request_rate_limit_exceeded` reste distinct de
`organization_token_quota_exhausted` et d'un `429` provenant du fournisseur IA.
Le frontend doit donc choisir son comportement avec `code`, jamais uniquement
avec le statut HTTP `429`.

<a id="rate-limit-order"></a>
## Ordre des contrôles

Pour `POST /api/messages` et `POST /api/messages/stream` :

1. ASP.NET valide l'authentification.
2. Le handler valide la commande.
3. Le service de contexte retrouve le membre et l'organisation internes.
4. Le rate limiting évalue atomiquement les règles membre et organisation.
5. Si une limite est atteinte, la requête s'arrête avec `429` sans incrément
   partiel des compteurs.
6. Sinon, les deux compteurs sont incrémentés et le lifecycle enregistre la
   question puis prépare le traitement.
7. L'agent peut ensuite démarrer les appels externes et produire sa réponse.

Avant l'étape 4, aucune identité fournie directement par le client n'est
utilisée pour construire une clé de limite. Après un refus à l'étape 5, les
étapes 6 et 7 ne démarrent pas.

<a id="rate-limit-v1-limits"></a>
## Limites de la première version

Cette première version assume explicitement une seule instance de l'API.

Elle ne fournit pas encore :

- de compteurs partagés entre plusieurs replicas;
- Redis ou un autre stockage distribué;
- de limite générale sur tous les endpoints de l'API;
- de limite d'orchestrations IA simultanées par organisation;
- de bail distribué avec expiration et libération idempotente;
- de stratégie `rate_limit_store_unavailable`, car le compteur ne dépend pas
  encore d'un service externe;
- de métriques distribuées de rate limiting;
- de tests simulant plusieurs instances de l'API.

Un redémarrage du processus remet également les compteurs mémoire à zéro. Ce
comportement est accepté pour la première version et ne doit pas être conservé
lorsque l'application passera à plusieurs instances.

<a id="rate-limit-distributed"></a>
## Évolution Redis et multi-instance

Lorsque l'API devra fonctionner avec plusieurs instances, l'interface de
stockage du rate limiting devra recevoir une implémentation Redis sans changer
la responsabilité des handlers.

Cette évolution devra au minimum ajouter :

- une acquisition atomique des règles membre et organisation avec expiration
  afin de conserver la même sémantique que la première version entre plusieurs
  replicas;
- des compteurs membre et organisation partagés par toutes les instances;
- une limite générale configurable sur les requêtes authentifiées si elle est
  encore nécessaire à ce moment-là;
- une limite d'orchestrations IA simultanées par organisation;
- des baux possédant un identifiant, une expiration et une libération
  idempotente afin qu'un crash ne bloque pas définitivement une organisation;
- une stratégie explicite lorsque Redis est indisponible. Pour les endpoints de
  génération coûteux, aucun appel au modèle ne doit démarrer si le système ne
  peut pas vérifier la limite;
- une réponse contrôlée `503 rate_limit_store_unavailable` dans ce scénario;
- des métriques par politique sans identifiant de membre ou d'organisation dans
  les labels;
- des tests de concurrence, d'expiration et de partage de compteur entre au
  moins deux instances simulées.

Le futur stockage Redis devra respecter le contrat applicatif existant plutôt
que déplacer la logique de décision dans les controllers ou dans les handlers.

<a id="rate-limit-frontend"></a>
## Frontend

Angular conserve la question, bloque temporairement l'envoi et affiche le
temps de reprise. Il ne confond pas cette erreur avec un quota mensuel épuisé.

Exemple : pour `request_rate_limit_exceeded` avec `retryAfterSeconds = 30`, le
bouton peut afficher `Réessayer dans 30 s`, rester désactivé pendant le compte à
rebours, puis redevenir disponible. Angular ne renvoie pas automatiquement la
question à la fin du délai; l'utilisateur confirme un nouvel envoi.

<a id="rate-limit-acceptance"></a>
## Critères d'acceptation de la première version

- `POST /api/messages` et `POST /api/messages/stream` partagent les mêmes règles
  de limite par organisation et par membre.
- Les limites proviennent de la configuration et sont validées au démarrage.
- Deux membres d'une même organisation possèdent des compteurs membre distincts
  et partagent le compteur organisation.
- Les règles membre et organisation sont acquises atomiquement : une requête
  refusée ne consomme aucun compteur partiellement.
- Une requête dépassant une limite retourne `429`,
  `request_rate_limit_exceeded`, `Retry-After` et `retryAfterSeconds`.
- Aucun lifecycle de message ni appel à l'agent ne démarre après un refus.
- La fenêtre expire correctement et le compteur accepte de nouveau les
  requêtes après son expiration.
- Les fenêtres expirées ne restent pas indéfiniment dans le store mémoire.
- Les accès concurrents au store mémoire ne permettent pas de dépasser la
  limite configurée dans une seule instance.
- Le code dépend d'une abstraction de stockage afin de permettre une
  implémentation Redis ultérieure.
