USE [AssistantCoreDb];

-- Operation de purge generique, pour toute categorie de retention autre que
-- Conversation (deja servie par ConversationPurgeRequest). Le couple
-- (OrganizationId, Scope, TargetId) identifie de facon unique la cible purgee :
-- deux demandes identiques n'ouvrent jamais deux operations.
IF OBJECT_ID(N'[dbo].[PurgeOperation]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[PurgeOperation]
    (
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
        [Scope] NVARCHAR(20) NOT NULL,
        [TargetId] UNIQUEIDENTIFIER NOT NULL,
        [RequestedAt] DATETIMEOFFSET NOT NULL,
        [PurgeAfter] DATETIMEOFFSET NOT NULL,
        [Status] NVARCHAR(20) NOT NULL,
        [Step] NVARCHAR(60) NOT NULL,
        [LeaseId] UNIQUEIDENTIFIER NULL,
        [LeaseExpiresAt] DATETIMEOFFSET NULL,
        [AttemptCount] INT NOT NULL CONSTRAINT [DF_PurgeOperation_AttemptCount] DEFAULT (0),
        [NextAttemptAt] DATETIMEOFFSET NULL,
        [LastErrorCode] NVARCHAR(100) NULL,
        [CompletedAt] DATETIMEOFFSET NULL,
        CONSTRAINT [PK_PurgeOperation] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PurgeOperation_Scope]
            CHECK ([Scope] IN (N'Usage', N'Search', N'Ingestion', N'Logs', N'Audit')),
        CONSTRAINT [CK_PurgeOperation_Status]
            CHECK ([Status] IN (
                N'Pending', N'Processing', N'TemporaryFailure', N'PermanentFailure', N'Completed'))
    );

    CREATE UNIQUE INDEX [IX_PurgeOperation_OrganizationId_Scope_TargetId]
        ON [dbo].[PurgeOperation]([OrganizationId], [Scope], [TargetId]);

    CREATE INDEX [IX_PurgeOperation_Claim]
        ON [dbo].[PurgeOperation]([Status], [PurgeAfter], [NextAttemptAt], [LeaseExpiresAt]);
END;
