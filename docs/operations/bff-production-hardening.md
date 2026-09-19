# Migration BFF et durcissement production

## Table des matières

- [But](#bff-production-purpose)
- [Architecture cible](#bff-production-target)
- [Principes de migration](#bff-production-principles)
- [Étape 1 — Fondation BFF](#bff-production-step-1)
- [Étape 2 — Basculer la SPA vers la session BFF](#bff-production-step-2)
- [Étape 3 — Proxy BFF vers l’API métier](#bff-production-step-3)
- [Étape 4 — Rendre l’API métier interne](#bff-production-step-4)
- [Étape 5 — Séparer l’entrée webhook publique](#bff-production-step-5)
- [Étape 6 — Limiter les abus sur les endpoints publics](#bff-production-step-6)
- [Étape 7 — Réduire les privilèges Azure et SQL](#bff-production-step-7)
- [Étape 8 — Fermer la fenêtre ACL Microsoft 365](#bff-production-step-8)
- [Étape 9 — Durcir les conteneurs, HTTP et la chaîne CI/CD](#bff-production-step-9)
- [Étape 10 — Front Door / WAF et exposition production](#bff-production-step-10)
- [Étape 11 — Validation finale avant production](#bff-production-step-11)

<a id="bff-production-purpose"></a>
## But

Faire évoluer l’application depuis une SPA qui détient un access token Entra vers une architecture BFF où le navigateur ne manipule plus les tokens d’accès. La cible doit aussi réduire la surface publique du backend, limiter les privilèges Azure/SQL et rendre les points d’entrée publics résistants aux abus avant l’utilisation avec des données client réelles.

Le backoffice actuellement anonyme est volontairement exclu de cette migration à la demande de l’équipe et doit être traité séparément.

<a id="bff-production-target"></a>
## Architecture cible

```text
Internet
   |
   v
Azure Front Door + WAF
   |
   +-------------------------+
   |                         |
   v                         v
SPA + BFF                Graph Webhook
public                   public minimal
   |                         |
   | cookie de session       |
   | HttpOnly/Secure         |
   +------------+------------+
                |
                v
        AssistantCore.Service
             interne
                |
       +--------+---------+
       |        |         |
       v        v         v
     Worker   Azure SQL  AI Search / Foundry / Graph
```

Le navigateur connaît les routes BFF dont il a besoin, mais il ne reçoit plus d’access token Entra ou Microsoft Graph. Le BFF utilise une session cookie et acquiert les tokens côté serveur. L’API métier reste protégée par son authentification Bearer et ses règles de scope/rôle, mais elle n’est plus directement exposée à Internet.

<a id="bff-production-principles"></a>
## Principes de migration

1. Chaque étape doit laisser CERTIF récupérable et testable.
2. Le BFF est ajouté avant de retirer MSAL de la SPA.
3. La SPA est basculée vers le BFF avant de rendre l’API interne.
4. Le webhook public est séparé avant de fermer l’ingress public de l’API métier.
5. Les contrôles d’autorisation de l’API restent actifs même lorsque l’API devient interne.
6. Les changements réseau et identité sont faits après validation fonctionnelle du nouveau flow d’authentification.
7. Les secrets, identités et droits SQL sont réduits avant l’exposition à de vraies données client.

<a id="bff-production-step-1"></a>
## Étape 1 — Fondation BFF

Créer un projet ASP.NET Core dédié au BFF.

### Flow détaillé

1. Le navigateur appelle `/bff/login`.
2. Le BFF déclenche le challenge OpenID Connect vers Entra.
3. Entra renvoie le navigateur au callback OIDC du BFF.
4. Le BFF établit une session serveur et remet au navigateur uniquement un cookie de session.
5. Le cookie porte `HttpOnly`, `Secure`, `SameSite=Lax` et un nom préfixé `__Host-`.
6. `/bff/session` permet à la SPA de savoir si l’utilisateur est connecté sans recevoir le token Entra.
7. `/bff/csrf` fournit un token antiforgery utilisé pour les opérations mutantes.
8. `/bff/logout` valide l’antiforgery puis ferme la session locale et la session Entra.

### Résultat concret

Le BFF peut authentifier un utilisateur sans remettre l’access token au code Angular.

### Réalisation pas à pas

- Ajouter `AssistantCore.Bff` en .NET 10.
- Configurer Microsoft Identity Web en mode web app confidentielle.
- Configurer le cookie de session sécurisé.
- Ajouter les endpoints login/session/csrf/logout.
- Ajouter le build du BFF à la CI.
- Ajouter ensuite des tests d’intégration dédiés au BFF.

### Critères d’acceptation

- Le navigateur reçoit un cookie de session HttpOnly/Secure.
- Aucun access token n’est renvoyé par les endpoints BFF.
- Une URL de retour externe ne peut pas être utilisée comme open redirect.
- Les opérations mutantes BFF sont protégées par antiforgery.

<a id="bff-production-step-2"></a>
## Étape 2 — Basculer la SPA vers la session BFF

Retirer progressivement l’authentification MSAL du navigateur.

### Flow détaillé

1. Au démarrage, Angular appelle `/bff/session` avec les credentials same-origin.
2. Si la session n’existe pas, l’UI redirige vers `/bff/login`.
3. Après le retour Entra, la SPA recharge son état depuis `/bff/session` et `/api/auth/me` via le BFF.
4. Angular ne lit, ne stocke et n’injecte plus aucun access token.

### Réalisation pas à pas

- Introduire un provider d’authentification BFF côté SPA.
- Remplacer `MsalGuard` et `MsalInterceptor` pour les appels métier.
- Supprimer l’usage production de `sessionStorage` pour les tokens.
- Conserver le mode JWT local uniquement si nécessaire pour le développement local, puis le faire passer lui aussi derrière le BFF si possible.
- Configurer les appels Angular en same-origin et `withCredentials` seulement si une phase transitoire cross-origin est nécessaire.

### Critères d’acceptation

- Aucun header `Authorization: Bearer` n’est émis par le navigateur vers l’API métier.
- Aucun access token n’apparaît dans `sessionStorage` ou `localStorage` en CERTIF/PROD.
- Login, refresh, expiration de session et logout fonctionnent.

<a id="bff-production-step-3"></a>
## Étape 3 — Proxy BFF vers l’API métier

Le BFF devient l’unique point d’entrée des appels métier de la SPA.

### Flow détaillé

1. Angular appelle `/api/...` sur la même origine que le BFF.
2. Le BFF vérifie la session cookie et l’antiforgery pour les méthodes mutantes.
3. Le BFF acquiert côté serveur un access token Entra pour `AssistantCore.Service`.
4. Le BFF appelle l’API métier avec `Authorization: Bearer <token>`.
5. L’API continue de valider audience, scope, rôles et règles tenant.
6. Le BFF retransmet uniquement les headers/réponses nécessaires au navigateur.

### Réalisation pas à pas

- Ajouter un client downstream typé pour `AssistantCore.Service`.
- Acquérir les tokens via `ITokenAcquisition`/Microsoft Identity Web.
- Implémenter un proxy contrôlé pour les routes métier nécessaires.
- Ne jamais relayer les cookies navigateur à l’API métier.
- Filtrer les headers hop-by-hop et les headers sensibles.
- Préserver streaming/SSE pour `/api/messages`.
- Ajouter timeout, annulation et taille maximale des requêtes.

### Critères d’acceptation

- L’API reçoit un Bearer token valide émis pour elle.
- Le navigateur ne reçoit jamais ce Bearer token.
- Le streaming des réponses fonctionne via le BFF.
- Les erreurs 401/403/429 restent correctement traduites dans l’UI.

<a id="bff-production-step-4"></a>
## Étape 4 — Rendre l’API métier interne

Après validation du proxy BFF, retirer l’accès Internet direct à `AssistantCore.Service`.

### Réalisation pas à pas

- Mettre l’ingress de l’API métier en mode interne dans Azure Container Apps.
- Garder le BFF en ingress externe HTTPS.
- Garder le Worker sans ingress.
- Configurer le BFF pour appeler le FQDN interne de l’API.
- Vérifier que les health probes internes continuent de fonctionner.
- Vérifier qu’une requête Internet directe vers l’ancien endpoint API n’aboutit plus.

### Critères d’acceptation

- Seuls le BFF et les composants du réseau/environnement autorisé peuvent atteindre l’API métier.
- L’application reste entièrement fonctionnelle depuis la SPA.

<a id="bff-production-step-5"></a>
## Étape 5 — Séparer l’entrée webhook publique

Microsoft Graph doit continuer à joindre un endpoint public même lorsque l’API métier devient interne.

### Réalisation pas à pas

- Créer un petit point d’entrée public dédié au webhook Graph, séparé de l’API métier.
- Conserver la validation `subscriptionId`, tenant et `clientState` HMAC.
- Ne lui donner accès qu’aux dépendances réellement nécessaires.
- Faire persister ou transmettre le signal de synchronisation vers le backend interne.
- Modifier `Microsoft365:WebhookBaseUrl` vers l’URL publique dédiée.

### Critères d’acceptation

- Microsoft Graph valide et appelle le nouvel endpoint.
- Une notification valide déclenche toujours la delta synchronization.
- Le webhook n’expose aucune route métier.

<a id="bff-production-step-6"></a>
## Étape 6 — Limiter les abus sur les endpoints publics

L’audit a identifié un risque d’amplification SQL sur le webhook : une requête peut contenir de nombreuses notifications et provoquer de nombreuses recherches SQL.

### Réalisation pas à pas

- Limiter la taille HTTP du body webhook.
- Limiter explicitement le nombre de notifications acceptées par requête.
- Ajouter un rate limit externe sur webhook, health et autres endpoints publics légitimes.
- Étudier un chargement en lot des subscriptions au lieu d’une requête SQL séquentielle par notification.
- Garder `/health/live` très léger.
- Éviter que `/health/ready` devienne un endpoint public coûteux ; le réserver aux probes internes ou ajouter une protection réseau.

### Critères d’acceptation

- Un payload excessif est rejeté avant des milliers d’accès SQL.
- Les probes Azure continuent de fonctionner.
- Les endpoints publics ont une limite documentée.

<a id="bff-production-step-7"></a>
## Étape 7 — Réduire les privilèges Azure et SQL

L’API et le Worker ne doivent pas utiliser le compte administrateur SQL et ne doivent pas partager une identité capable de lire tous les secrets.

### Réalisation pas à pas

- Créer une identité managée distincte pour le BFF, l’API, le Worker et les migrations.
- Donner à chaque identité uniquement les secrets Key Vault dont elle a besoin.
- Réserver les droits DDL SQL au job de migrations.
- Donner à l’API et au Worker uniquement les droits DML nécessaires.
- Migrer de la connexion SQL administrateur vers Entra/identités managées ou des principals SQL séparés à privilèges minimaux.
- Retirer progressivement la règle SQL large `Allow Azure Services`.
- Ajouter un Private Endpoint SQL/VNet lorsque l’environnement réseau cible est prêt.

### Critères d’acceptation

- Une compromission du BFF ne donne pas accès aux secrets du Worker ou des migrations.
- L’API ne peut pas modifier le schéma SQL.
- Le Worker ne peut pas administrer SQL.

<a id="bff-production-step-8"></a>
## Étape 8 — Fermer la fenêtre ACL Microsoft 365

L’index Azure AI Search contient des ACL mais une permission récemment révoquée peut rester utilisable jusqu’à la prochaine réconciliation.

### Réalisation pas à pas

- Brancher la revérification d’accès déjà disponible dans le chemin final de retrieval.
- Vérifier les résultats candidats avant de renvoyer du contenu à l’agent/utilisateur.
- Échouer de façon fermée lorsque la revérification d’accès est impossible pour un contenu sensible.
- Mesurer l’impact de latence et mettre en cache uniquement les décisions d’accès à durée courte si nécessaire.

### Critères d’acceptation

- Un utilisateur auquel l’accès vient d’être retiré ne reçoit plus le contenu au-delà de la fenêtre explicitement acceptée.
- Une panne du vérificateur ne provoque pas une fuite par défaut.

<a id="bff-production-step-9"></a>
## Étape 9 — Durcir les conteneurs, HTTP et la chaîne CI/CD

### Réalisation pas à pas

- Exécuter API/BFF/Worker avec un utilisateur non-root.
- Activer HSTS sur les endpoints publics de production.
- Ajouter CSP adaptée à la SPA et aux flows Entra.
- Ajouter `Cache-Control: no-store` sur les réponses d’authentification/session sensibles.
- Ne faire confiance qu’aux proxies Azure/Front Door attendus pour les forwarded headers.
- Valider `Operation-Location` OCR avant de renvoyer une clé API vers cette URL.
- Épingler les GitHub Actions critiques sur des SHA lorsque l’équipe est prête à maintenir ces références.
- Ajouter scan d’image, SBOM et idéalement signature/attestation des images de release.

### Critères d’acceptation

- Les conteneurs ne tournent pas en root.
- Un client direct ne peut pas falsifier les forwarded headers de confiance.
- La CI produit des artefacts inspectables et les images critiques sont scannées.

<a id="bff-production-step-10"></a>
## Étape 10 — Front Door / WAF et exposition production

Front Door devient le point d’entrée public commun lorsque le BFF et le webhook dédié sont prêts.

### Réalisation pas à pas

- Créer les routes publiques vers le BFF et le webhook.
- Ajouter WAF managed rules.
- Ajouter rate limits adaptés au login, au webhook et aux endpoints coûteux.
- Utiliser des domaines publics stables.
- Restreindre les origines Container Apps afin qu’elles ne soient pas contournables directement lorsque l’architecture réseau le permet.
- Configurer certificats, DNS et redirect URIs Entra de production.

### Critères d’acceptation

- La SPA/BFF est accessible par le domaine production.
- Le webhook Graph reste accessible.
- L’API métier n’est pas directement exposée.
- Les règles WAF ne cassent ni OIDC, ni SSE, ni webhooks Graph.

<a id="bff-production-step-11"></a>
## Étape 11 — Validation finale avant production

Exécuter les tickets manuels de readiness déjà créés et ajouter les validations BFF spécifiques.

### Vérifications obligatoires

- Aucun access token Entra dans DevTools Storage.
- Aucun Bearer token envoyé par Angular.
- Cookie de session `HttpOnly`, `Secure`, `SameSite=Lax`.
- CSRF refusé sur une mutation sans token antiforgery.
- Login/logout/session expirée fonctionnels.
- API inaccessible directement depuis Internet.
- Webhook Graph fonctionnel avec API interne.
- Worker sans ingress et reprise du backlog validée.
- SQL utilise des identités à privilèges minimaux.
- ACL révoquée testée manuellement.
- Logs sans token, secret ni contenu sensible inutile.
- Rollback et restauration SQL vérifiés.

### Go production

La mise en production peut être envisagée lorsque les étapes 1 à 8 sont terminées et validées, que les tests manuels critiques passent et qu’aucune fuite de données/ACL ou perte d’ingestion n’est observée. Les éléments avancés de supply-chain de l’étape 9 peuvent être terminés juste après la première release pilote si les contrôles de base sont déjà en place, mais le BFF, l’API interne, le webhook protégé, le least privilege SQL/Key Vault et la vérification ACL font partie de la cible avant données client sensibles.
