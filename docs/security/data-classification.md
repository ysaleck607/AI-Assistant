# Classification des données sensibles et politique de chiffrement

## Table des matières

- [But](#but)
- [Les trois mécanismes de protection](#mecanismes)
- [Les quatre niveaux de classification](#niveaux)
- [Classer une nouvelle colonne](#classer)
- [Colonnes protégées — niveaux A, B et C](#inventaire-sensible)
- [Colonnes autorisées en clair — niveau D](#inventaire-clair)
- [État actuel de la protection](#etat-actuel)
- [Portée : base, exports, sauvegardes et journaux](#portee)
- [Arbitrages assumés](#arbitrages)
- [Gouvernance](#gouvernance)

<a id="but"></a>
## But

Ce document dit, pour chaque colonne persistée en SQL Server, si elle doit être
chiffrée, hachée ou laissée en clair, et pourquoi. Il est écrit avant les
migrations techniques : il sert de contrat commun à
[#170](https://github.com/SypnatixAI/AI-Assistant/issues/170) (chiffrement
applicatif et rotation), [#172](https://github.com/SypnatixAI/AI-Assistant/issues/172)
(PII des membres) et [#173](https://github.com/SypnatixAI/AI-Assistant/issues/173)
(payloads Microsoft 365).

Le chiffrement au repos du disque et de SQL Server reste nécessaire, mais il ne
protège pas contre une lecture directe de la base par un compte privilégié, une
fuite d'export logique, une mauvaise exposition par un outil d'administration,
ni une erreur applicative qui retourne une colonne sensible. C'est ce que le
chiffrement applicatif couvre, et c'est la raison d'être de cette
classification.

Chiffrer toutes les colonnes rendrait la base impossible à requêter. N'en
protéger qu'une laisserait le reste des données du client en clair. Ce document
tranche entre les deux, colonne par colonne.

<a id="mecanismes"></a>
## Les trois mécanismes de protection

La confusion entre ces trois mécanismes est la principale source d'erreur. Ils
ne répondent pas à la même question.

| Mécanisme | Réversible | À quoi ça sert | Ce que ça coûte |
| --- | --- | --- | --- |
| **Chiffrement applicatif** | Oui, avec la clé | Conserver une valeur qu'il faudra relire telle quelle : contenu d'un message, titre d'un document | La colonne ne peut plus être triée, filtrée par intervalle, ni jointe |
| **Hachage irréversible** | Non | Vérifier une valeur sans la conserver : comparer un `clientState` reçu à celui attendu | La valeur d'origine est définitivement perdue |
| **Blind index HMAC** | Non | Retrouver une ligne par égalité exacte alors que la valeur est chiffrée : chercher un membre par courriel | Ne permet que l'égalité stricte, jamais une recherche partielle ni un tri |

Trois règles en découlent :

1. **Jamais de chiffrement déterministe** pour préserver une recherche. Un
   chiffrement déterministe produit le même chiffré pour la même valeur et
   révèle donc les doublons et la distribution des données. Quand la recherche
   exacte est nécessaire, c'est un blind index HMAC dans une colonne séparée,
   pas un chiffrement affaibli.
2. **Jamais de secret réversible en clair.** Si seule la comparaison est
   nécessaire, on hache.
3. **Les clés ne vivent jamais dans SQL Server**, sinon le chiffrement
   applicatif ne protège plus de rien face à un accès à la base.

<a id="niveaux"></a>
## Les quatre niveaux de classification

**Niveau A — contenu métier du client.** Tout ce qui provient de
l'environnement du client et peut révéler son activité : contenu d'un message,
titre d'un document, nom d'une bibliothèque, valeurs d'un élément de liste. Chez
un cabinet comptable, ces valeurs portent des noms de clients, des montants et
des périodes fiscales. **Chiffrement applicatif requis.**

**Niveau B — données personnelles.** Nom, courriel, UPN : identifient une
personne physique. **Chiffrement applicatif requis**, avec blind index HMAC là
où une recherche exacte ou une contrainte d'unicité existe.

**Niveau C — secrets et valeurs de validation.** Valeurs dont la connaissance
donne un pouvoir : `clientState` d'un webhook, `state` de consentement. **Ne
jamais conserver en clair** ; hacher si seule la comparaison est nécessaire.

**Niveau D — identifiants techniques et métadonnées d'exécution.**
Identifiants Microsoft, états, dates, compteurs, empreintes déjà calculées.
**Clair autorisé.** Ces valeurs restent soumises à la gouvernance des données,
mais les chiffrer casserait index, jointures et requêtes sans gain
proportionné.

<a id="classer"></a>
## Classer une nouvelle colonne

Dans l'ordre, la première réponse « oui » donne le niveau :

1. La valeur donne-t-elle un accès ou sert-elle à valider un appel ? → **C**
2. Identifie-t-elle une personne physique ? → **B**
3. Provient-elle du contenu ou de la structure de l'environnement du client ? → **A**
4. Sinon → **D**

Une colonne dont le niveau se discute est classée au niveau le plus protecteur
jusqu'à arbitrage écrit dans [Arbitrages assumés](#arbitrages).

<a id="inventaire-sensible"></a>
## Colonnes protégées — niveaux A, B et C

Propriétaire « Client » signifie que la donnée appartient à l'organisation
cliente et doit disparaître avec elle ; « Synaptix » qu'elle appartient à
l'exploitant du service.

| Table | Colonne | Niveau | Raison | Stratégie | Recherche / index | Rétention | Propriétaire |
| --- | --- | --- | --- | --- | --- | --- | --- |
| Message | `Content` | A | Contenu conversationnel du client | Chiffrement applicatif | Aucune | Cycle de vie de la conversation | Client |
| Conversation | `Title` | A | Reprend souvent la question posée | Chiffrement applicatif | Aucune | Cycle de vie de la conversation | Client |
| Conversation | `ContextSummary` | A | Résumé du contenu échangé | Chiffrement applicatif | Aucune | Cycle de vie de la conversation | Client |
| MessageWarning | `Content` | A | Peut citer le contexte métier | Chiffrement applicatif | Aucune | Cycle de vie du message | Client |
| MessageSource | `Title` | A | Titre d'un document du client | Chiffrement applicatif | Aucune | Cycle de vie du message | Client |
| MessageSource | `Reference` | A | Référence interne du document cité | Chiffrement applicatif | Aucune | Cycle de vie du message | Client |
| MessageSource | `Url` | A | URL SharePoint révélant l'arborescence | Chiffrement applicatif | Aucune | Cycle de vie du message | Client |
| Microsoft365ListItemWork | `FieldsJson` | A | Contient toutes les colonnes d'un élément de liste : PII, finance, RH | Chiffrement applicatif | Aucune | Jusqu'au traitement du travail | Client |
| Microsoft365DocumentWork | `Name` | A | Nom de fichier du client | Chiffrement applicatif | Aucune | Jusqu'au traitement du travail | Client |
| Microsoft365DocumentWork | `WebUrl` | A | Arborescence SharePoint | Chiffrement applicatif | Aucune | Jusqu'au traitement du travail | Client |
| Microsoft365ListItemWork | `WebUrl` | A | Arborescence SharePoint | Chiffrement applicatif | Aucune | Jusqu'au traitement du travail | Client |
| Microsoft365IndexedContent | `Title` | A | Titre d'un document indexé | Chiffrement applicatif | Aucune | Tant que le contenu est indexé | Client |
| Microsoft365IndexedContent | `WebUrl` | A | Arborescence SharePoint | Chiffrement applicatif | Aucune | Tant que le contenu est indexé | Client |
| Microsoft365IndexedContent | `SiteUrl` | A | URL du site du client | Chiffrement applicatif | Aucune | Tant que le contenu est indexé | Client |
| Microsoft365Source | `WebUrl` | A | URL de la bibliothèque ou du site | Chiffrement applicatif | Aucune | Tant que la source existe | Client |
| Microsoft365Source | `DeltaLink` | A | Jeton opaque contenant le chemin de la ressource | Chiffrement applicatif | Aucune | Jusqu'au checkpoint suivant | Client |
| Microsoft365ReindexOperation | `Reason` | A | Texte libre saisi par un opérateur, peut nommer le client | Chiffrement applicatif | Aucune | Voir [#112](https://github.com/SypnatixAI/AI-Assistant/issues/112) | Synaptix |
| OrganizationMember | `Name` | B | Donnée personnelle directement identifiante | Chiffrement applicatif | Aucune | Cycle de vie du membre | Client |
| OrganizationMember | `Email` | B | Donnée personnelle, sert aux recherches exactes | Chiffrement applicatif + blind index HMAC | Égalité exacte via le blind index | Cycle de vie du membre | Client |
| Microsoft365Drive | `OwnerUserPrincipalName` | B | Un UPN est une adresse de connexion, donc une PII | Chiffrement applicatif | Aucune | Tant que le OneDrive est enregistré | Client |
| Microsoft365Subscription | `ProtectedClientState` | C | Valide l'authenticité d'une notification Graph | HMAC-SHA256, clé hors SQL | Comparaison en temps constant | Cycle de vie de la souscription | Synaptix |
| Microsoft365Connection | `ConsentStateHash` | C | Empreinte du `state` de consentement, à usage unique | Déjà haché | Égalité sur l'empreinte | Expiration courte du `state` | Synaptix |

### Colonnes volontairement laissées en clair malgré une sensibilité réelle

| Table | Colonne | Décision | Justification |
| --- | --- | --- | --- |
| Microsoft365Source | `DisplayName` | Clair | Sert de clé de tri des bibliothèques dans l'administration et le backoffice. Voir [Arbitrages assumés](#arbitrages). |
| Organization | `Name`, `Domain` | Clair | Identité de l'organisation cliente, nécessaire à l'exploitation et déjà connue de Synaptix. |
| AdministrativeAuditEntry | `OldValues`, `NewValues` | Clair | Une liste blanche limite déjà ces colonnes à des champs non sensibles ; les y chiffrer rendrait l'audit inexploitable. Toute extension de la liste blanche doit repasser par ce document. |

<a id="inventaire-clair"></a>
## Colonnes autorisées en clair — niveau D

Les 24 entités persistées sont inventoriées. Les colonnes ci-dessous sont
classées D : identifiants techniques, états, dates, compteurs et empreintes
déjà calculées.

| Table | Colonnes en clair |
| --- | --- |
| AdministrativeAuditEntry | `Id`, `OrganizationId`, `ActorType`, `ActorId`, `Action`, `TargetType`, `TargetId`, `OccurredAt`, `CorrelationId` |
| Conversation | `Id`, `OrganizationId`, `OwnerMemberId`, `Status`, `Version`, `CreatedAt`, `UpdatedAt`, `DeletedAt`, `ContextSummaryUpdatedAt` |
| ConversationPurgeRequest | toutes : `Id`, `ConversationId`, `OrganizationId`, `RequestedAt`, `PurgeAfter`, `Status`, `Step`, `LeaseId`, `LeaseExpiresAt`, `AttemptCount`, `NextAttemptAt`, `LastErrorCode`, `CompletedAt`, `DeletedMessageCount`, `DeletedSourceCount` |
| Message | `Id`, `ConversationId`, `Role`, `ProcessingStatus`, `Model`, `ProcessingErrorCode`, `CreatedAt`, `UpdatedAt` |
| MessageSource | `Id`, `MessageId`, `SourceType`, `SourceDate` |
| MessageWarning | `Id`, `MessageId` |
| Microsoft365Connection | `Id`, `OrganizationId`, `OrganizationConnectorId`, `TenantId`, `ConsentStateExpiresAt`, `ConsentStateConsumedAt`, `ConsentValidatedAt`, `OnboardingCompletedAt`, `LastErrorCode`, `CreatedAt`, `UpdatedAt`, `RowVersion` |
| Microsoft365DocumentWork | `Id`, `OrganizationId`, `Microsoft365SourceId`, `Microsoft365SynchronizationId`, `SiteId`, `DriveId`, `DriveItemId`, `ETag`, `CreatedDateTime`, `LastModifiedDateTime`, `Size`, `MimeType`, `DeduplicationKey`, `WorkType`, `AttemptCount`, `LeaseId`, `LeaseExpiresAt`, `NextAttemptAt`, `CompletedAt`, `LastErrorCode`, `CreatedAt` |
| Microsoft365Drive | `OrganizationId`, `OrganizationConnectorId`, `SiteId`, `DriveId`, `OwnerUserObjectId` |
| Microsoft365IndexedContent | `Id`, `OrganizationId`, `Microsoft365SourceId`, `ExternalContentId`, `DocumentVersion`, `LastModifiedAt`, `AclFingerprint`, `IsAvailable`, `NextAclReconciliationAt`, `CreatedAt`, `UpdatedAt` |
| Microsoft365IndexedPassage | toutes : `Id`, `Microsoft365IndexedContentId`, `ChunkId` |
| Microsoft365List | toutes : `OrganizationId`, `OrganizationConnectorId`, `SiteId`, `ListId`, `SchemaFingerprint`, `RequiresItemReprocessing` |
| Microsoft365ListItemWork | `Id`, `OrganizationId`, `Microsoft365SourceId`, `Microsoft365SynchronizationId`, `SiteId`, `ListId`, `ListItemId`, `ETag`, `CreatedDateTime`, `LastModifiedDateTime`, `DeduplicationKey`, `WorkType`, `CreatedAt` |
| Microsoft365ReindexOperation | `Id`, `OrganizationId`, `Microsoft365ConnectionId`, `RequestedByOperatorId`, `SourceCount`, `CompletedSourceCount`, `DiscoveredDocumentCount`, `ProcessedDocumentCount`, `IgnoredDocumentCount`, `FailedDocumentCount`, `RequestedAt`, `StartedAt`, `CompletedAt`, `LastErrorCode` |
| Microsoft365Site | toutes : `OrganizationId`, `OrganizationConnectorId`, `SiteId` |
| Microsoft365Source | `Id`, `Microsoft365ConnectionId`, `Kind`, `ExternalResourceId`, `ParentExternalResourceId`, `Status`, `StatusBeforeUnavailable`, `IsIndexed`, `DiscoveredAt`, `EnabledAt`, `LastSuccessfulSynchronizationAt`, `LastSynchronizationAttemptAt`, `NextSynchronizationAt`, `LastErrorCode`, `SynchronizationLeaseId`, `SynchronizationLeaseExpiresAt` |
| Microsoft365Subscription | `Id`, `Microsoft365SourceId`, `OrganizationId`, `Resource`, `MicrosoftSubscriptionId`, `ExpiresAt`, `LastRenewedAt`, `LastErrorCode`, `CreatedAt`, `UpdatedAt` |
| Microsoft365Synchronization | toutes : `Id`, `Microsoft365SourceId`, `Microsoft365ReindexOperationId`, `AttemptCount`, `CreatedCount`, `ModifiedCount`, `DeletedCount`, `IgnoredCount`, `FailedCount`, `RequestedAt`, `StartedAt`, `CompletedAt`, `LastErrorCode` |
| Organization | `Id`, `IdentityProvider`, `ExternalTenantId`, `Status`, `CreatedAt` |
| OrganizationConnector | toutes : `Id`, `OrganizationId`, `Type`, `Status`, `IsConfigured` |
| OrganizationConnectorSource | toutes : `OrganizationConnectorId`, `Status`, `IsIndexed` |
| OrganizationMember | `Id`, `OrganizationId`, `EmailLookupHash`, `IdentityProvider`, `ExternalUserId`, `Role`, `Status`, `LastSuccessfulAuthenticationAt`, `Version` |
| PurgeOperation | toutes : `Id`, `OrganizationId`, `Scope`, `TargetId`, `RequestedAt`, `PurgeAfter`, `Status`, `Step`, `LeaseId`, `LeaseExpiresAt`, `AttemptCount`, `NextAttemptAt`, `LastErrorCode`, `CompletedAt` |
| TokenConsumption | toutes : `Id`, `OrganizationId`, `AssistantMessageId`, `PeriodStartsAt`, `PeriodEndsAt`, `InputTokens`, `OutputTokens`, `TotalTokens`, `CreatedAt` |

`ExternalUserId` reste en clair volontairement : c'est l'`oid` Microsoft Entra,
identité stable et non secrète du membre, et il sert de clé de jointure. C'est
`Name` et `Email` qui portent la charge personnelle.

<a id="etat-actuel"></a>
## État actuel de la protection

| Colonne | État | Livré par |
| --- | --- | --- |
| `Message.Content` | Chiffré | PR #302 |
| `Conversation.Title` | Chiffré | PR #302 |
| `MessageWarning.Content` | Chiffré | PR #302 |
| `Microsoft365Subscription.ProtectedClientState` | HMAC | migration V01_028 |
| `Microsoft365Connection.ConsentStateHash` | Haché | dès l'origine |
| Toutes les autres colonnes A et B du tableau | **En clair** | reste à faire |

Le socle technique existe : `IFieldEncryptor`, `IFieldEncryptorFactory`,
`EncryptedStringConverter` et l'intégration ASP.NET Core Data Protection. Les
identifiants de purpose se terminent par `.v1`, ce qui laisse la place à une
rotation versionnée.

Deux dépendances d'infrastructure conditionnent la suite :

- le stockage durable et partagé des clés, faute de quoi deux instances
  n'utilisent pas la même clé et le contenu chiffré devient illisible — objet de
  [#307](https://github.com/SypnatixAI/AI-Assistant/issues/307) ;
- la politique de rotation, objet de
  [#170](https://github.com/SypnatixAI/AI-Assistant/issues/170).

<a id="portee"></a>
## Portée : base, exports, sauvegardes et journaux

Le chiffrement applicatif s'applique **avant** l'écriture en base. Les valeurs
protégées le restent donc automatiquement dans un export logique, un dump ou
une sauvegarde, puisque la base ne contient jamais la valeur en clair.

Trois conséquences à tenir :

- **Sauvegardes** : les sauvegardes restent chiffrées au repos, et une
  restauration n'a de valeur que si les clés sont restaurables elles aussi. Une
  sauvegarde sans sa clé est un fichier illisible. L'exercice de restauration de
  [#116](https://github.com/SypnatixAI/AI-Assistant/issues/116) doit donc
  couvrir la restauration des clés, pas seulement celle de la base.
- **Journaux** : aucune valeur de niveau A, B ou C ne doit apparaître dans un
  log, ni en clair ni chiffrée. Les journaux existants tracent des
  identifiants, des états et des compteurs : c'est la règle à maintenir.
- **Outils d'administration** : une lecture directe de la base montre désormais
  du chiffré pour les colonnes A et B. C'est l'effet recherché ; le diagnostic
  passe par le backoffice, pas par SQL.

<a id="arbitrages"></a>
## Arbitrages assumés

**`Microsoft365Source.DisplayName` reste en clair.** Le nom d'une bibliothèque
révèle une part de l'organisation interne du client, ce qui plaide pour le
niveau A. Mais cette colonne sert de clé de tri dans l'administration et le
backoffice, et le chiffrement applicatif interdit tout `ORDER BY`. Le nom d'une
bibliothèque SharePoint est jugé nettement moins sensible que son contenu, que
protègent `FieldsJson`, `Title` et les `WebUrl`. À revoir si un client exige la
confidentialité de sa nomenclature.

**Les durées de rétention ne sont pas fixées ici.** La colonne « Rétention »
renvoie au cycle de vie de l'entité porteuse. Les durées chiffrées relèvent de
[#112](https://github.com/SypnatixAI/AI-Assistant/issues/112), qui les rend
explicites et configurables. Les inventer ici créerait deux sources de vérité.

**Propriétaire.** « Client » pour toute donnée provenant de l'environnement du
client ou désignant ses membres ; « Synaptix » pour les données produites par
l'exploitation du service. Une donnée « Client » doit disparaître avec
l'organisation ; une donnée « Synaptix » suit la rétention d'exploitation.

<a id="gouvernance"></a>
## Gouvernance

**Toute nouvelle colonne persistée est classée avant d'entrer dans le schéma.**
Concrètement : une migration qui ajoute une colonne s'accompagne d'une ligne
dans ce document, dans le tableau du niveau retenu. Une migration qui ajoute
une colonne non classée est incomplète.

La règle vaut aussi pour une colonne existante dont l'usage change : élargir la
liste blanche d'audit, stocker une nouvelle valeur Graph dans une colonne
existante ou étendre un payload de connecteur demande de reclasser la colonne
ici avant de livrer.
