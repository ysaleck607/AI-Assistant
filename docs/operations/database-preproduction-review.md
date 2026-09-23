# Pre-production database review

## Scope

This review targets the schema that should exist before the first production database is created. Because no production database exists yet, obsolete pre-production structures can be removed instead of being carried forever as compatibility debt.

## Changes already applied

### Removed from current scope

- Token quota accounting has been removed end-to-end.
- `TokenConsumption` is removed from the domain model, repository layer, API, configuration and pre-production schema.
- `Conversation.ContextSummary` and `ContextSummaryUpdatedAt` are removed because no runtime component consumes them.

### Conversation storage

- Conversation history supplied to the agent is bounded to the ten most recent completed messages (roughly five user/assistant exchanges).
- Microsoft 365 sourced assistant messages are no longer excluded from rehydrated history.
- The redundant `(OrganizationId, OwnerMemberId, Id)` conversation index is removed.
- Active conversation listing uses a filtered index on `(OrganizationId, OwnerMemberId, Status, UpdatedAt, Id) WHERE DeletedAt IS NULL`.

### Sensitive source metadata

`MessageSource.Title`, `Reference` and `Url` are client-owned metadata and are now application-encrypted. The pre-production transition migration deletes historical plaintext `MessageSource` rows before enabling the encrypted representation. Production must start from the clean baseline and must never depend on this destructive transition step.

### SQL-backed queues

`Microsoft365DocumentWork`, `Microsoft365ListItemWork`, `PurgeOperation` and `ConversationPurgeRequest` each have separate filtered indexes for pending work, retryable failures and expired processing leases. This matches the actual worker claim predicates and keeps serializable claim transactions away from unrelated completed rows.

`Microsoft365ListItemWork` is now a complete durable queue rather than append-only storage. It has processing status, attempt count, lease ownership/expiry, retry scheduling, completion timestamp and error-code fields. Existing pre-production rows are migrated to `Pending` so their delta payloads are preserved.

Document and list-item work also have `(Status, CompletedAt)` retention indexes. Successes and permanent failures both receive a terminal `CompletedAt` timestamp.

### Microsoft 365 lookup/indexing

Dedicated `(OrganizationId, SiteId)` indexes were added for sites, drives and lists because these are the actual discovery/read access paths. ACL reconciliation now uses `(NextAclReconciliationAt, UpdatedAt)`.

`OrganizationConnectorSource.Status` is normalized to the same enum-name storage convention (`Active` / `Inactive`) used by the rest of the schema.

Microsoft Lists now have an end-to-end consumer path:

1. list delta synchronization persists deduplicated list-item work;
2. the ingestion worker claims work with a lease;
3. business fields are flattened into stable searchable text;
4. list-item permissions are resolved through the existing SharePoint ACL resolver;
5. chunks and embeddings are written to Azure AI Search with `sourceType = microsoft-list`;
6. indexed-content metadata and ACL reconciliation state are registered in SQL;
7. delete delta events remove both Azure AI Search passages and SQL indexed-content metadata;
8. transient failures are retried and permanent failures stop consuming the queue.

### Ingestion retention

`Retention.IngestionRetentionDays` is now enforced by the ingestion worker. Each maintenance cycle can delete at most 500 terminal rows from each ingestion-work table. Only `Completed` and `PermanentFailure` rows with `CompletedAt` older than the configured cutoff are eligible; `Pending`, `Processing` and `TemporaryFailure` rows are never removed by this cleanup.

`V01_050` backfills a conservative terminal timestamp for pre-existing permanent failures and creates the retention indexes used by this cleanup.

## Citation/source attribution

Final answer sources are no longer selected with a language-specific stop-word list. The application performs deterministic claim-to-evidence attribution using:

- local TF-IDF similarity between each answer claim and each retrieved evidence item;
- exact structured anchors (amounts, dates, invoice/project identifiers, percentages, etc.);
- Azure AI Search relevance when available.

The generative agent does not choose which sources the UI displays.

## Flyway rebaseline strategy

Do not hand-write the production baseline from the current migration history. The safe sequence is:

1. create an empty disposable SQL database;
2. run the complete current Flyway chain through `V01_050` plus repeatable migrations;
3. run repository/application integration tests against that database;
4. compare the resulting SQL schema with the EF Core model and correct any drift;
5. export the resulting schema-only SQL;
6. turn that schema into the new production baseline migration;
7. create another empty database using only the new baseline plus repeatables and rerun the same validation;
8. only then archive/remove the historical pre-production migrations and recreate DEV/CERTIF from the clean baseline.

The first production environment must be created from the validated baseline, not by replaying the historical development chain.

## Pre-production reset expectation

Because this branch intentionally removes obsolete structures and encrypts source metadata that used to be plaintext, DEV/CERTIF databases should be treated as disposable during the rebaseline. Preserve only test fixtures that are intentionally regenerated; do not attempt to promote pre-production database contents into production.
