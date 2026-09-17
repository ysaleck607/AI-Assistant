# Envoyer un message à l’assistant

## Table des matières

- [But](#but)
- [Contrat HTTP](#contrat)
- [Flow détaillé](#flow)
- [Répartition des responsabilités](#responsabilites)
- [Recherche Microsoft 365](#recherche-m365)
- [Analyse des classeurs Excel](#analyse-classeurs)
- [Sécurité](#securite)
- [Persistance et historique](#persistance)
- [Réponse progressive](#streaming)
- [Limites](#limites)
- [Références](#references)

<a id="but"></a>
## But

Permettre à un membre authentifié de poser une question et de recevoir une
réponse produite par l’agent Foundry, avec les sources internes auxquelles ce
membre a réellement accès.

Le backend reste la frontière de confiance. Il gère l’identité, les quotas, la
conversation, l’autorisation des outils, les filtres Microsoft 365, la
persistance et les erreurs. Foundry choisit le modèle, décide s’il doit appeler
un outil et construit la réponse finale.

<a id="contrat"></a>
## Contrat HTTP

Les routes disponibles sont :

```http
POST /api/messages
POST /api/messages/stream
Authorization: Bearer <access_token>
Content-Type: application/json
```

Le corps ne permet plus au client de choisir un modèle :

```json
{
  "conversationId": null,
  "message": "Quel est le code du projet Atlas ?"
}
```

`conversationId` est facultatif. Une valeur nulle crée une conversation; une
valeur existante ajoute le message après vérification de son propriétaire.

La réponse complète contient l’identifiant de la conversation et du message,
la réponse, l’identifiant de l’agent réellement utilisé, les sources, les
avertissements et la date de création. Le champ historique nommé `model` est
conservé dans le contrat de réponse pour l’audit, mais il contient désormais
l’identifiant retourné par Foundry et n’est jamais accepté dans la requête.

<a id="flow"></a>
## Flow détaillé

1. `MessagesController` reçoit la requête et envoie une commande au
   `IDispatcher`.
2. Le handler valide le membre, le quota et le nombre de traitements simultanés.
3. `MessageProcessingLifecycleService` crée ou retrouve la conversation,
   enregistre le message utilisateur et le marque `InProgress`.
4. Pour une conversation existante, le repository charge au maximum les 20
   derniers messages, puis les remet dans l’ordre chronologique.
5. `FoundryAgentRuntime` obtient les outils autorisés pour l’organisation. Il
   expose `EnterpriseSearch`, `QueryOutlookMailbox` et `AnalyzeSpreadsheet`
   lorsque l’identité Microsoft du membre est complète et que les sources
   correspondantes sont configurées.
6. Le client Foundry envoie l’historique et la question à la version configurée
   de l’agent. Cette version déclare les outils visibles par le modèle; le
   backend fournit uniquement les implémentations autorisées pour le tour. Au
   démarrage, il vérifie que les trois fonctions attendues sont bien déclarées.
7. Si Foundry appelle `EnterpriseSearch`, le backend valide l’appel et le route
   vers le connecteur Microsoft 365. Foundry ne reçoit jamais de credential ni
   la possibilité de construire lui-même un filtre d’autorisation.
8. Le connecteur résout en parallèle les groupes Entra et SharePoint, construit
   le filtre ACL obligatoire et interroge la Knowledge Base Azure AI Search.
9. Le backend applique encore le seuil de pertinence, une vérification ACL après
   recherche, la normalisation et la limite finale de sources avant de remettre
   les preuves à Foundry.
10. Pour une question sur les derniers courriels, une période, un expéditeur ou
    la présence d'un message, `QueryOutlookMailbox` consulte la boîte de
    l'utilisateur Entra authentifié. Le modèle ne choisit jamais l'identifiant
    de la boîte et ne reçoit aucun jeton Graph. La requête est triée par date de
    réception ou d'envoi et retourne au maximum le nombre demandé. Pour une
    simple liste, l'outil retourne uniquement les aperçus. Lorsque la réponse
    exige le contenu exact, il récupère en un seul lot Graph le corps des
    messages retenus, sans télécharger celui de tous les candidats examinés.
11. Pour une analyse tabulaire, le backend localise le classeur indexé, revérifie
    ses permissions, le télécharge depuis Microsoft Graph et exécute les calculs
    et filtres sur toutes ses lignes sans transmettre le classeur au modèle.
12. Foundry peut poursuivre ses appels d’outil, puis produit la réponse finale.
13. Le backend enregistre atomiquement la réponse, l’identifiant de l’agent,
    l’usage et les sources collectées, marque la question `Completed` et libère
    la place d’exécution.

Avant l’opération, aucun nouveau message n’existe. Après un succès, la question
et la réponse sont persistées dans la même conversation. Après un échec, aucune
fausse réponse Assistant n’est créée et le message utilisateur conserve un état
d’échec exploitable.

<a id="responsabilites"></a>
## Répartition des responsabilités

| Composant | Responsabilité |
| --- | --- |
| Frontend | Envoyer la conversation et le texte; afficher le flux, les sources et le quota |
| Handler applicatif | Orchestrer le cycle de vie, le quota et l’appel au runtime |
| Runtime Foundry | Autoriser les outils et servir de pont entre Foundry et le routeur interne |
| Agent Foundry | Choisir le modèle, planifier les appels d’outil et rédiger la réponse |
| Connecteur Microsoft 365 | Construire le filtre sécurisé et contrôler les preuves |
| Azure AI Search Knowledge Base | Décomposer la recherche et retourner les passages candidats |
| Analyseur de classeur | Lire les feuilles et exécuter les agrégats et filtres déterministes sur toutes les lignes |
| Repository | Isoler et persister conversations, messages, sources et usage |

L’ancien orchestrateur local, la sélection de modèle côté client, l’expansion de
requête locale, la fusion de résultats et le Corrective RAG local ne participent
plus à ce flow.

<a id="recherche-m365"></a>
## Recherche Microsoft 365

La recherche agentique Azure AI Search est obligatoire pour ce connecteur. Le
backend lui transmet :

- la question et l’historique utile;
- le nom de la Knowledge Base et de sa source;
- un filtre composé de l’organisation, de l’utilisateur, des groupes Entra, des
  groupes SharePoint et des filtres fonctionnels demandés;
- une limite large de candidats pour la recherche, distincte de la limite plus
  petite des preuves finales.

La Knowledge Base utilise le déploiement Azure OpenAI `gpt-5-mini` comme modèle
de planification. Le mode de raisonnement est `auto` par défaut : Azure commence
par une recherche légère et poursuit avec une planification par modèle lorsque
les premiers résultats ne suffisent pas. La valeur peut être forcée à `minimal`
ou `low` avec `AzureSearch__KnowledgeBaseRetrievalReasoningEffort`. Toute autre
valeur est refusée au démarrage; `low` et `auto` sont également refusés si le
modèle de planification n’est pas complètement configuré.

La Knowledge Base n’est pas exposée directement à Foundry par MCP pour le
moment. Un accès MCP direct déplacerait vers Foundry l’appel au moteur de
recherche. Il faudrait alors garantir que les filtres ACL obligatoires ne
peuvent jamais être omis ou modifiés et conserver la vérification après
recherche. Tant que ce contrat sécurisé n’est pas démontré, `EnterpriseSearch`
reste la façade contrôlée par le backend.

<a id="analyse-classeurs"></a>
## Analyse des classeurs Excel

`AnalyzeSpreadsheet` complète la recherche sémantique pour les questions qui
exigent un calcul global ou un filtrage exhaustif. Foundry doit utiliser cet
outil pour les moyennes, sommes, minima, maxima, comptes et comparaisons portant
sur l’ensemble d’un fichier XLSX ou XLSM.

Le modèle fournit uniquement des paramètres fonctionnels : nom du fichier, nom
de la feuille, agrégats, filtres et colonnes à retourner. Il ne fournit jamais
de tenant, d’identifiant Graph, de filtre ACL ou de requête SQL. Le backend :

1. résout les groupes Entra et SharePoint du membre;
2. localise le document parmi les contenus indexés autorisés;
3. revérifie ses permissions Microsoft 365 actuelles;
4. télécharge le fichier avec la limite de taille configurée;
5. lit toutes les lignes de la feuille dans les limites de cellules et de
   feuilles configurées;
6. calcule les agrégats avant d’appliquer les filtres;
7. retourne le nombre exact de correspondances et au plus 200 lignes ou 40 000
   caractères de résultat à Foundry.

Les filtres sont combinés avec `AND`. Un filtre peut comparer une cellule à une
valeur littérale ou au résultat d’un agrégat nommé. Par exemple, le backend peut
calculer la moyenne de `Transaction Amount` sur toutes les lignes, puis conserver
les lignes dont ce montant est supérieur à cette moyenne et qui satisfont les
autres seuils numériques.

Avant l’opération, Foundry ne possède que le nom fonctionnel du fichier. Après
l’opération, il reçoit un résultat JSON borné avec les agrégats, le nombre total
de lignes, le nombre exact de correspondances et les lignes retenues. Il ne
reçoit ni le fichier brut ni les lignes qui ne correspondent pas.

<a id="securite"></a>
## Sécurité

- Le controller injecte uniquement `IDispatcher`.
- L’organisation et le membre proviennent du token, jamais du corps JSON.
- Une conversation absente ou appartenant à un autre membre retourne le même
  résultat afin de ne pas révéler son existence.
- L’outil Microsoft 365 n’est pas exposé sans identité Entra et courriel valides.
- Un échec de résolution des groupes ou de la Knowledge Base arrête la recherche;
  aucun chemin moins sécurisé n’est utilisé en repli.
- Les documents sont revérifiés après la recherche avant d’être envoyés à
  Foundry.
- Un classeur est également revérifié avant son téléchargement; une permission
  retirée bloque l’analyse même si ses passages sont encore présents dans
  l’index.

<a id="persistance"></a>
## Persistance et historique

Le backend persiste le message utilisateur avant l’appel distant. La finalisation
regroupe la réponse Assistant, les sources, l’usage et le changement d’état. Le
contexte long généré auparavant par un second appel de modèle n’est plus mis à
jour. Le runtime utilise une fenêtre bornée aux 20 derniers messages afin de
réduire la latence et le volume de jetons.

<a id="streaming"></a>
## Réponse progressive

`POST /api/messages/stream` retourne des Server-Sent Events. Les événements
principaux sont `message.accepted`, `activity.delta`, `activity.completed`,
`answer.delta`, `answer.reset`, `answer.completed` et `error`. Le backend vide le
buffer après chaque événement. La route non progressive applique les mêmes
règles de sécurité et de persistance.

<a id="limites"></a>
## Limites

- Les sources retournées correspondent aujourd’hui aux preuves collectées par
  les appels d’outil, pas encore à une liste structurée des seules citations
  utilisées dans le texte final.
- Le vectorizer Azure AI Search et le worker utilisent le même déploiement
  `m365-text-embedding-3-small`, le même modèle `text-embedding-3-small` et
  `1536` dimensions. Un changement de l’un de ces paramètres exige une
  réindexation contrôlée avant sa mise en service.
- Les autres connecteurs futurs devront être exposés un par un avec la même
  validation backend; Foundry ne reçoit pas automatiquement toutes les
  intégrations disponibles.
- L’analyse tabulaire prend en charge XLSX et XLSM. Les feuilles masquées sont
  ignorées, les formules utilisent leur dernière valeur enregistrée et le
  backend ne recalcule pas le classeur.
- Au-delà de 200 correspondances ou 40 000 caractères de résultat, le compte
  reste exact mais seule la première partie des lignes est remise au modèle afin
  de borner les jetons.

<a id="references"></a>
## Références

- [Agentic retrieval dans Azure AI Search](https://learn.microsoft.com/azure/search/agentic-retrieval-overview)
- [Exécuter une requête de Knowledge Base](https://learn.microsoft.com/azure/search/agentic-retrieval-how-to-retrieve)
- [Configurer un vectorizer](https://learn.microsoft.com/azure/search/vector-search-how-to-configure-vectorizer)
