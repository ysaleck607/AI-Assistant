# Activer un client dans AssistantCore

## Table des matières

- [But](#client-onboarding-purpose)
- [Principe de reprise](#client-onboarding-retry)
- [Informations à obtenir](#client-onboarding-inputs)
- [Étapes côté Synaptix](#client-onboarding-synaptix)
- [Étapes côté client](#client-onboarding-customer)
- [Premier accès et création du membre](#client-onboarding-first-login)
- [Connexion Microsoft 365](#client-onboarding-microsoft365)
- [Vérification finale](#client-onboarding-verification)
- [Reprendre après une étape incomplète](#client-onboarding-recovery)
- [État avant et après](#client-onboarding-state)
- [Limites](#client-onboarding-limits)

<a id="client-onboarding-purpose"></a>
## But

Ce guide décrit toutes les actions nécessaires pour qu'un nouveau client puisse
ouvrir AssistantCore, connecter Microsoft 365 et rendre ses premières sources
SharePoint disponibles à la plateforme.

Il sépare clairement les actions réalisées par Synaptix des actions qui
appartiennent à l'administrateur Microsoft Entra du client.

<a id="client-onboarding-retry"></a>
## Principe de reprise

Les préparatifs administratifs peuvent être réalisés dans un ordre différent.
Par exemple, le client peut affecter les rôles Entra avant que Synaptix crée
l'organisation. Ces rôles seront lus lors de la prochaine authentification.

Les appels applicatifs conservent cependant leurs dépendances naturelles :

```text
Organisation connue
  -> premier administrateur authentifié
  -> consentement Microsoft 365 actif
  -> site découvert
  -> au moins une bibliothèque ou liste activée
  -> accès normal des autres membres
  -> worker et indexation en arrière-plan
```

Appeler une étape avant que sa dépendance soit disponible peut retourner une
erreur explicite, mais ne doit pas rendre les données inutilisables. Une fois la
dépendance corrigée, la même action peut être relancée. Le backend réutilise les
enregistrements valides et recrée uniquement les travaux manquants ou échoués.

Il ne faut jamais vider la base pour reprendre un onboarding.

<a id="client-onboarding-inputs"></a>
## Informations à obtenir

Synaptix demande au client :

- son domaine de courriel principal, par exemple `contoso.com`;
- l'identité de son premier administrateur AssistantCore;
- l'identité des autres utilisateurs ou groupes qui auront accès;
- les sites SharePoint qui devront être indexés;
- une confirmation que l'administrateur peut accorder le consentement Microsoft 365.

Le domaine doit correspondre au suffixe de courriel utilisé par le premier
utilisateur. Lors du premier accès, le backend associe ce domaine au claim `tid`
du tenant Microsoft Entra qui a authentifié l'utilisateur.

<a id="client-onboarding-synaptix"></a>
## Étapes côté Synaptix

### 1. Préparer l'Enterprise Application du client

Vérifier que l'App Registration AssistantCore expose au minimum les app roles :

- `AssistantCore.Access`, qui autorise l'entrée dans la plateforme;
- `TenantAdmin`, qui donne les droits d'administration de la plateforme et
  permet de terminer la configuration Microsoft 365.

L'Enterprise Application correspondante doit être présente dans le tenant du
client. Si elle n'existe pas encore, elle est créée lorsque l'administrateur du
client accepte l'application ou lorsqu'un administrateur Entra la crée depuis
le portail.

### 2. Créer l'organisation AssistantCore

Envoyer :

```http
POST /api/organizations
Content-Type: application/json
Authorization: Bearer <jeton ManagementAdmin>

{
  "domain": "contoso.com"
}
```

Cette opération est idempotente. Si l'organisation active existe déjà pour ce
domaine, le backend retourne la même organisation au lieu d'exiger une
suppression ou une correction manuelle en base.

Cet endpoint est protégé par la policy `ManagementAdmin` depuis que le
backoffice onPremia l'expose (page Organisations, action « Créer une
organisation »). Un jeton d'opérateur Synaptix est désormais requis pour
l'appeler, y compris manuellement via Postman.

L'organisation peut être créée avant ou après l'affectation des utilisateurs
et des rôles dans Microsoft Entra. Aucun membre interne n'est requis à cette
étape.

### 3. Confirmer l'environnement

Vérifier que le frontend et l'API pointent vers le même environnement :

- DEV pour les essais avec les services simulés;
- CERTIF pour la validation avec Microsoft Entra et Microsoft Graph réels.

Ne jamais créer le client dans un environnement puis lui transmettre l'URL
d'un autre environnement.

<a id="client-onboarding-customer"></a>
## Étapes côté client

### 1. Autoriser les personnes dans Microsoft Entra

Dans **Microsoft Entra ID > Enterprise applications > AssistantCore > Users
and groups**, le client affecte :

- `AssistantCore.Access` à tous les utilisateurs ou groupes autorisés;
- `TenantAdmin` au premier administrateur, en plus de
  `AssistantCore.Access`.

L'ordre entre cette affectation et la création de l'organisation n'est pas
important. Si l'affectation est modifiée plus tard, les droits effectifs
changent dès que l'utilisateur obtient un nouveau jeton. Le rôle indicatif en
base n'est pas resynchronisé.

Un rôle natif Microsoft comme `Global Administrator` ne remplace pas
`TenantAdmin`. Il peut permettre d'accorder le consentement Microsoft, mais il
ne donne pas automatiquement les droits d'administration dans AssistantCore.

### 2. Ouvrir AssistantCore avec le premier administrateur

Le premier administrateur ouvre l'URL transmise par Synaptix et se connecte
avec son compte du tenant client.

Le jeton doit contenir :

- le tenant client dans `tid`;
- l'utilisateur dans `oid`;
- `AssistantCore.Access`;
- `TenantAdmin`.

<a id="client-onboarding-first-login"></a>
## Premier accès et création du membre

Le frontend appelle l'endpoint d'authentification AssistantCore. Le backend :

1. cherche d'abord l'organisation par le tenant `tid`;
2. si le tenant n'est pas encore associé, cherche l'organisation par le
   domaine du courriel;
3. associe une seule fois le tenant à cette organisation;
4. crée le membre interne s'il n'existe pas;
5. dérive son rôle effectif depuis les app roles du jeton, sans modifier le
   rôle indicatif en base;
6. autorise l'administrateur à continuer même si Microsoft 365 n'est pas encore
   configuré.

Si l'utilisateur tente cette étape avant la création de l'organisation, l'API
refuse temporairement l'accès sans créer de données orphelines. Après la
création de l'organisation, il suffit de se reconnecter.

<a id="client-onboarding-microsoft365"></a>
## Connexion Microsoft 365

### 1. Accorder le consentement

Depuis l'écran d'administration, le premier administrateur démarre le
consentement. Le frontend appelle :

```http
POST /api/microsoft365/consent
Authorization: Bearer <jeton administrateur>
```

Le backend crée ou remet en état la connexion en `PendingConsent`, puis
retourne l'URL Microsoft. L'administrateur accepte les permissions. Microsoft
rappelle ensuite l'API, qui valide le tenant et place la connexion en `Active`.

Un consentement refusé, expiré ou interrompu peut être redémarré. La nouvelle
tentative remplace l'ancien `state`; les enregistrements déjà créés ne doivent
pas être supprimés.

### 2. Choisir un site SharePoint

Après le consentement, l'administrateur consulte les sites disponibles et en
sélectionne au moins un. Le backend :

1. enregistre ou actualise le site;
2. découvre ses bibliothèques et ses listes;
3. enregistre ou actualise chaque source;
4. active les sources compatibles;
5. crée les synchronisations et abonnements manquants.

Si Microsoft Graph échoue après l'enregistrement du site ou après l'activation
d'une partie des sources, l'administrateur sélectionne de nouveau le même site.
Le backend conserve les étapes réussies et reprend les autres sans doublon.

### 3. Laisser le worker terminer

Le worker réclame les synchronisations en attente, y compris les anciennes
synchronisations `Running` dont le lease a expiré. Il traite les bibliothèques
et les listes séparément afin qu'une source en erreur n'impose pas de vider la
base.

L'onboarding est considéré terminé uniquement lorsque :

- le consentement Microsoft 365 est actif;
- au moins un site est enregistré;
- au moins une bibliothèque ou liste enfant est réellement activée pour
  l'indexation et se trouve dans un état exploitable.

Une simple ligne de site créée avant un échec de découverte ne suffit pas à
débloquer les utilisateurs standards.

<a id="client-onboarding-verification"></a>
## Vérification finale

Synaptix vérifie :

1. `GET /api/microsoft365/onboarding` retourne `isComplete: true`;
2. `isConsentComplete`, `hasSelectedSite` et `hasIndexedSource` valent `true`;
3. les synchronisations ne restent pas indéfiniment à `Running`;
4. au moins une source activée possède une synchronisation initiale réussie ou
   en cours de reprise;
5. un utilisateur qui possède seulement `AssistantCore.Access` peut maintenant
   ouvrir la plateforme;
6. une recherche peut retrouver le contenu indexé auquel cet utilisateur a
   accès.

Le client vérifie avec un utilisateur standard distinct de l'administrateur.
Cette vérification confirme que l'admission normale est débloquée et que les
permissions Microsoft 365 restent appliquées.

<a id="client-onboarding-recovery"></a>
## Reprendre après une étape incomplète

| Situation observée | Correction | Action à relancer |
| --- | --- | --- |
| L'utilisateur a ses rôles, mais l'organisation manque | Synaptix crée l'organisation | L'utilisateur se reconnecte |
| L'organisation existe déjà | Aucune suppression | Relancer la création ou continuer |
| `TenantAdmin` a été ajouté après le premier accès | Attendre un nouveau jeton Entra | Déconnexion puis reconnexion |
| Le consentement a été refusé ou a expiré | Corriger les permissions | Redémarrer le consentement |
| Le site existe, mais aucune source n'est active | Corriger l'accès Graph | Sélectionner de nouveau le même site |
| Une synchronisation est en échec permanent | Corriger la cause externe | Réactiver ou resélectionner la source |
| Une synchronisation est restée à `Running` après un arrêt | Redémarrer le worker | Le worker la reprend après expiration du lease |
| Une souscription Microsoft Graph est en erreur | Corriger l'accès ou la configuration du webhook | Laisser tourner la maintenance du worker |

Les états techniques doivent être vérifiés à l'aide des outils d'observabilité
et des accès opérationnels approuvés. Les lignes d'onboarding ne doivent pas
être supprimées manuellement; toute intervention de ce type doit rester
exceptionnelle et comprise.

<a id="client-onboarding-state"></a>
## État avant et après

Avant l'onboarding, le tenant et les utilisateurs existent seulement dans
Microsoft Entra. AssistantCore ne connaît pas encore leur organisation et ne
peut lire aucune source Microsoft 365.

Après l'onboarding :

- l'organisation active est associée au tenant client;
- les membres sont créés lors de leur premier accès et leur rôle effectif est
  ensuite dérivé de chaque jeton courant;
- la connexion Microsoft 365 est active;
- au moins une source enfant est activée;
- le worker peut reprendre les synchronisations et les traitements;
- les utilisateurs standards autorisés peuvent accéder à la plateforme.

<a id="client-onboarding-limits"></a>
## Limites

- Il est impossible d'accorder le consentement Microsoft 365 avant de connaître
  l'organisation dans AssistantCore, car le consentement doit être rattaché à
  une organisation précise. Une tentative prématurée peut toutefois être
  relancée sans nettoyage de la base.
- L'affectation des rôles et groupes dans Microsoft Entra peut demander quelques
  minutes avant d'apparaître dans un nouveau jeton.
- La présence d'une source activée termine l'onboarding administratif; la durée
  nécessaire pour indexer tous ses documents dépend de leur nombre et des
  services externes.
