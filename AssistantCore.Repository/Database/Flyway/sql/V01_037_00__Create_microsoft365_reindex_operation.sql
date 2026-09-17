USE [AssistantCoreDb];

IF OBJECT_ID(N'[dbo].[Microsoft365ReindexOperation]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Microsoft365ReindexOperation]
    (
        [Id] UNIQUEIDENTIFIER NOT NULL,
        [OrganizationId] UNIQUEIDENTIFIER NOT NULL,
        [Microsoft365ConnectionId] UNIQUEIDENTIFIER NOT NULL,
        [RequestedByOperatorId] UNIQUEIDENTIFIER NOT NULL,
        [Reason] NVARCHAR(500) NULL,
        [Status] NVARCHAR(30) NOT NULL,
        [SourceCount] INT NOT NULL,
        [CompletedSourceCount] INT NOT NULL,
        [DiscoveredDocumentCount] INT NOT NULL,
        [ProcessedDocumentCount] INT NOT NULL,
        [IgnoredDocumentCount] INT NOT NULL,
        [FailedDocumentCount] INT NOT NULL,
        [RequestedAt] DATETIMEOFFSET NOT NULL,
        [StartedAt] DATETIMEOFFSET NULL,
        [CompletedAt] DATETIMEOFFSET NULL,
        [LastErrorCode] NVARCHAR(100) NULL,
        CONSTRAINT [PK_Microsoft365ReindexOperation] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Microsoft365ReindexOperation_Organization_OrganizationId]
            FOREIGN KEY ([OrganizationId]) REFERENCES [dbo].[Organization]([Id]),
        CONSTRAINT [FK_Microsoft365ReindexOperation_Connection_Microsoft365ConnectionId]
            FOREIGN KEY ([Microsoft365ConnectionId]) REFERENCES [dbo].[Microsoft365Connection]([Id]),
        CONSTRAINT [CK_Microsoft365ReindexOperation_Status]
            CHECK ([Status] IN (N'Pending', N'Running', N'Succeeded', N'TemporaryFailure', N'PermanentFailure'))
    );

    CREATE INDEX [IX_Microsoft365ReindexOperation_OrganizationId_RequestedAt]
        ON [dbo].[Microsoft365ReindexOperation]([OrganizationId], [RequestedAt]);
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Microsoft365ReindexOperation_OneActivePerOrganization'
      AND object_id = OBJECT_ID(N'[dbo].[Microsoft365ReindexOperation]')
)
BEGIN
    -- SET explicite : un index filtre exige ANSI_NULLS et QUOTED_IDENTIFIER a ON
    -- au moment du CREATE. Le pilote utilise par Flyway ne garantit pas ces
    -- options, et un defaut different ferait echouer la migration avec l'erreur
    -- 1934 seulement lors du deploiement.
    -- Cet index empeche deux reprises completes simultanees sur la meme organisation,
    -- meme si deux operateurs Synaptix confirment au meme moment.
    EXEC(N'
        SET ANSI_NULLS ON;
        SET QUOTED_IDENTIFIER ON;
        CREATE UNIQUE INDEX [IX_Microsoft365ReindexOperation_OneActivePerOrganization]
            ON [dbo].[Microsoft365ReindexOperation]([OrganizationId])
            WHERE [Status] IN (N''Pending'', N''Running'');
    ');
END;

IF COL_LENGTH(N'[dbo].[Microsoft365Synchronization]', N'Microsoft365ReindexOperationId') IS NULL
BEGIN
    ALTER TABLE [dbo].[Microsoft365Synchronization]
        ADD [Microsoft365ReindexOperationId] UNIQUEIDENTIFIER NULL;
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.foreign_keys
    WHERE name = N'FK_Microsoft365Synchronization_ReindexOperation_Microsoft365ReindexOperationId'
      AND parent_object_id = OBJECT_ID(N'[dbo].[Microsoft365Synchronization]')
)
BEGIN
    -- EXEC : la colonne vient d'etre ajoutee dans le meme lot, sa resolution doit
    -- etre repoussee a l'execution.
    EXEC(N'
        ALTER TABLE [dbo].[Microsoft365Synchronization]
            ADD CONSTRAINT [FK_Microsoft365Synchronization_ReindexOperation_Microsoft365ReindexOperationId]
                FOREIGN KEY ([Microsoft365ReindexOperationId])
                REFERENCES [dbo].[Microsoft365ReindexOperation]([Id]);
    ');
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_Microsoft365Synchronization_Microsoft365ReindexOperationId'
      AND object_id = OBJECT_ID(N'[dbo].[Microsoft365Synchronization]')
)
BEGIN
    EXEC(N'
        CREATE INDEX [IX_Microsoft365Synchronization_Microsoft365ReindexOperationId]
            ON [dbo].[Microsoft365Synchronization]([Microsoft365ReindexOperationId]);
    ');
END;
