USE [AssistantCoreDb];

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Token quota/usage accounting is intentionally outside the current product scope.
IF OBJECT_ID(N'[dbo].[TokenConsumption]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [dbo].[TokenConsumption];
END;

-- These summary columns were introduced speculatively and are not used by the runtime.
-- Keeping unused sensitive conversation text in the schema would create unnecessary
-- retention and encryption obligations.
IF COL_LENGTH(N'[dbo].[Conversation]', N'ContextSummary') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Conversation] DROP COLUMN [ContextSummary];
END;

IF COL_LENGTH(N'[dbo].[Conversation]', N'ContextSummaryUpdatedAt') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Conversation] DROP COLUMN [ContextSummaryUpdatedAt];
END;

-- MessageSource metadata is client content (document/email title, internal reference and
-- SharePoint/Outlook URL) and is now application-encrypted. Existing pre-production rows
-- were stored in plaintext and cannot be transparently interpreted as Data Protection
-- payloads, so discard only these derived historical source rows before enabling the new
-- representation. This migration is intentionally pre-production-only and will be folded
-- into the clean baseline before the first production database is created.
IF OBJECT_ID(N'[dbo].[MessageSource]', N'U') IS NOT NULL
BEGIN
    DELETE FROM [dbo].[MessageSource];

    ALTER TABLE [dbo].[MessageSource] ALTER COLUMN [Title] NVARCHAR(MAX) NOT NULL;
    ALTER TABLE [dbo].[MessageSource] ALTER COLUMN [Reference] NVARCHAR(MAX) NOT NULL;
    ALTER TABLE [dbo].[MessageSource] ALTER COLUMN [Url] NVARCHAR(MAX) NULL;
END;

-- Conversation listing always excludes soft-deleted rows. Replace the broad index with
-- a smaller filtered index that matches that hot path. The older Organization/Owner/Id
-- index is redundant with the PK for point lookups and the listing index for collection
-- reads, so remove it to reduce write amplification.
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Conversation]')
      AND [name] = N'IX_Conversation_OrganizationId_OwnerMemberId_Id')
BEGIN
    DROP INDEX [IX_Conversation_OrganizationId_OwnerMemberId_Id]
        ON [dbo].[Conversation];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Conversation]')
      AND [name] = N'IX_Conversation_OrganizationId_OwnerMemberId_Status_UpdatedAt_Id')
BEGIN
    DROP INDEX [IX_Conversation_OrganizationId_OwnerMemberId_Status_UpdatedAt_Id]
        ON [dbo].[Conversation];
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Conversation]')
      AND [name] = N'IX_Conversation_ActiveListing')
BEGIN
    CREATE INDEX [IX_Conversation_ActiveListing]
        ON [dbo].[Conversation]
        ([OrganizationId], [OwnerMemberId], [Status], [UpdatedAt], [Id])
        WHERE [DeletedAt] IS NULL;
END;

-- Document work is a SQL-backed queue with three different eligibility paths. Separate
-- filtered indexes let SQL Server seek the relevant subset instead of scanning a single
-- mixed Status/NextAttemptAt index (which could not efficiently cover expired leases).
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]')
      AND [name] = N'IX_Microsoft365DocumentWork_Status_NextAttemptAt_CreatedAt')
BEGIN
    DROP INDEX [IX_Microsoft365DocumentWork_Status_NextAttemptAt_CreatedAt]
        ON [dbo].[Microsoft365DocumentWork];
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]')
      AND [name] = N'IX_Microsoft365DocumentWork_Pending_CreatedAt')
BEGIN
    CREATE INDEX [IX_Microsoft365DocumentWork_Pending_CreatedAt]
        ON [dbo].[Microsoft365DocumentWork] ([CreatedAt])
        WHERE [Status] = N'Pending';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]')
      AND [name] = N'IX_Microsoft365DocumentWork_RetryDue')
BEGIN
    CREATE INDEX [IX_Microsoft365DocumentWork_RetryDue]
        ON [dbo].[Microsoft365DocumentWork] ([NextAttemptAt], [CreatedAt])
        WHERE [Status] = N'TemporaryFailure';
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]')
      AND [name] = N'IX_Microsoft365DocumentWork_ExpiredLease')
BEGIN
    CREATE INDEX [IX_Microsoft365DocumentWork_ExpiredLease]
        ON [dbo].[Microsoft365DocumentWork] ([LeaseExpiresAt], [CreatedAt])
        WHERE [Status] = N'Processing';
END;

-- Microsoft Lists used to persist delta work without any consumer state. Promote that
-- table to the same durable lease/retry queue shape as document work. Existing pre-prod
-- rows represent valid unprocessed deltas, so they are backfilled as Pending rather than
-- discarded.
IF OBJECT_ID(N'[dbo].[Microsoft365ListItemWork]', N'U') IS NOT NULL
BEGIN
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'Status') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [Status] NVARCHAR(30) NULL;
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'AttemptCount') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork]
            ADD [AttemptCount] INT NOT NULL CONSTRAINT [DF_Microsoft365ListItemWork_AttemptCount] DEFAULT (0);
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'LeaseId') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [LeaseId] UNIQUEIDENTIFIER NULL;
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'LeaseExpiresAt') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [LeaseExpiresAt] DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'NextAttemptAt') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [NextAttemptAt] DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'CompletedAt') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [CompletedAt] DATETIMEOFFSET NULL;
    IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'LastErrorCode') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] ADD [LastErrorCode] NVARCHAR(200) NULL;

    EXEC sys.sp_executesql N'
    UPDATE [dbo].[Microsoft365ListItemWork]
    SET [Status] = N''Pending''
    WHERE [Status] IS NULL;

    ALTER TABLE [dbo].[Microsoft365ListItemWork]
        ALTER COLUMN [Status] NVARCHAR(30) NOT NULL;

    IF OBJECT_ID(N''[dbo].[CK_Microsoft365ListItemWork_Status]'', N''C'') IS NULL
        ALTER TABLE [dbo].[Microsoft365ListItemWork] WITH CHECK
            ADD CONSTRAINT [CK_Microsoft365ListItemWork_Status]
            CHECK ([Status] IN (N''Pending'', N''Processing'', N''Completed'', N''TemporaryFailure'', N''PermanentFailure''));

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N''[dbo].[Microsoft365ListItemWork]'')
          AND [name] = N''IX_Microsoft365ListItemWork_Pending_CreatedAt'')
        CREATE INDEX [IX_Microsoft365ListItemWork_Pending_CreatedAt]
            ON [dbo].[Microsoft365ListItemWork] ([CreatedAt])
            WHERE [Status] = N''Pending'';

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N''[dbo].[Microsoft365ListItemWork]'')
          AND [name] = N''IX_Microsoft365ListItemWork_RetryDue'')
        CREATE INDEX [IX_Microsoft365ListItemWork_RetryDue]
            ON [dbo].[Microsoft365ListItemWork] ([NextAttemptAt], [CreatedAt])
            WHERE [Status] = N''TemporaryFailure'';

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N''[dbo].[Microsoft365ListItemWork]'')
          AND [name] = N''IX_Microsoft365ListItemWork_ExpiredLease'')
        CREATE INDEX [IX_Microsoft365ListItemWork_ExpiredLease]
            ON [dbo].[Microsoft365ListItemWork] ([LeaseExpiresAt], [CreatedAt])
            WHERE [Status] = N''Processing'';';
END;

-- Purge workers use the same three eligibility paths. Filtered indexes keep the
-- serializable claim transaction focused on the small subset that can actually run.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[PurgeOperation]')
      AND [name] = N'IX_PurgeOperation_Status_PurgeAfter_NextAttemptAt_LeaseExpiresAt')
BEGIN
    DROP INDEX [IX_PurgeOperation_Status_PurgeAfter_NextAttemptAt_LeaseExpiresAt]
        ON [dbo].[PurgeOperation];
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[PurgeOperation]') AND [name] = N'IX_PurgeOperation_Pending_Due')
    CREATE INDEX [IX_PurgeOperation_Pending_Due]
        ON [dbo].[PurgeOperation] ([PurgeAfter], [RequestedAt])
        WHERE [Status] = N'Pending';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[PurgeOperation]') AND [name] = N'IX_PurgeOperation_Retry_Due')
    CREATE INDEX [IX_PurgeOperation_Retry_Due]
        ON [dbo].[PurgeOperation] ([NextAttemptAt], [PurgeAfter], [RequestedAt])
        WHERE [Status] = N'TemporaryFailure';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[PurgeOperation]') AND [name] = N'IX_PurgeOperation_ExpiredLease')
    CREATE INDEX [IX_PurgeOperation_ExpiredLease]
        ON [dbo].[PurgeOperation] ([LeaseExpiresAt], [PurgeAfter], [RequestedAt])
        WHERE [Status] = N'Processing';

IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]')
      AND [name] = N'IX_ConversationPurgeRequest_Status_PurgeAfter_NextAttemptAt_LeaseExpiresAt')
BEGIN
    DROP INDEX [IX_ConversationPurgeRequest_Status_PurgeAfter_NextAttemptAt_LeaseExpiresAt]
        ON [dbo].[ConversationPurgeRequest];
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]') AND [name] = N'IX_ConversationPurgeRequest_Pending_Due')
    CREATE INDEX [IX_ConversationPurgeRequest_Pending_Due]
        ON [dbo].[ConversationPurgeRequest] ([PurgeAfter], [RequestedAt])
        WHERE [Status] = N'Pending';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]') AND [name] = N'IX_ConversationPurgeRequest_Retry_Due')
    CREATE INDEX [IX_ConversationPurgeRequest_Retry_Due]
        ON [dbo].[ConversationPurgeRequest] ([NextAttemptAt], [PurgeAfter], [RequestedAt])
        WHERE [Status] = N'TemporaryFailure';
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [object_id] = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]') AND [name] = N'IX_ConversationPurgeRequest_ExpiredLease')
    CREATE INDEX [IX_ConversationPurgeRequest_ExpiredLease]
        ON [dbo].[ConversationPurgeRequest] ([LeaseExpiresAt], [PurgeAfter], [RequestedAt])
        WHERE [Status] = N'Processing';

-- ACL reconciliation orders by due time and then UpdatedAt, so preserve that order in
-- the index used by the batch reader.
IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365IndexedContent]')
      AND [name] = N'IX_Microsoft365IndexedContent_NextAclReconciliationAt')
BEGIN
    DROP INDEX [IX_Microsoft365IndexedContent_NextAclReconciliationAt]
        ON [dbo].[Microsoft365IndexedContent];
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365IndexedContent]')
      AND [name] = N'IX_Microsoft365IndexedContent_AclReconciliationDue')
BEGIN
    CREATE INDEX [IX_Microsoft365IndexedContent_AclReconciliationDue]
        ON [dbo].[Microsoft365IndexedContent] ([NextAclReconciliationAt], [UpdatedAt]);
END;

-- Site/drive/list discovery is scoped by organization and site. Existing uniqueness
-- indexes contain OrganizationConnectorId between those keys (or omit SiteId), which is
-- not the access shape used by these hot queries.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365Site]')
      AND [name] = N'IX_Microsoft365Site_OrganizationId_SiteId')
BEGIN
    CREATE INDEX [IX_Microsoft365Site_OrganizationId_SiteId]
        ON [dbo].[Microsoft365Site] ([OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365Drive]')
      AND [name] = N'IX_Microsoft365Drive_OrganizationId_SiteId')
BEGIN
    CREATE INDEX [IX_Microsoft365Drive_OrganizationId_SiteId]
        ON [dbo].[Microsoft365Drive] ([OrganizationId], [SiteId]);
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365List]')
      AND [name] = N'IX_Microsoft365List_OrganizationId_SiteId')
BEGIN
    CREATE INDEX [IX_Microsoft365List_OrganizationId_SiteId]
        ON [dbo].[Microsoft365List] ([OrganizationId], [SiteId]);
END;

-- Normalize legacy localized enum values before switching the EF converter to the same
-- enum-name storage convention used by the rest of the schema. The original schema also
-- constrained the localized values, so replace that constraint atomically.
IF OBJECT_ID(N'[dbo].[OrganizationConnectorSource]', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'[dbo].[CK_OrganizationConnectorSource_Status]', N'C') IS NOT NULL
    BEGIN
        ALTER TABLE [dbo].[OrganizationConnectorSource]
            DROP CONSTRAINT [CK_OrganizationConnectorSource_Status];
    END;

    UPDATE [dbo].[OrganizationConnectorSource]
    SET [Status] = N'Active'
    WHERE [Status] = N'Actif';

    UPDATE [dbo].[OrganizationConnectorSource]
    SET [Status] = N'Inactive'
    WHERE [Status] = N'Inactif';

    ALTER TABLE [dbo].[OrganizationConnectorSource] WITH CHECK
        ADD CONSTRAINT [CK_OrganizationConnectorSource_Status]
        CHECK ([Status] IN (N'Active', N'Inactive'));
END;

COMMIT TRANSACTION;
