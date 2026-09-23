USE [AssistantCoreDb];

-- La purge doit pouvoir reprendre a la derniere etape confirmee apres un arret
-- brutal. Les colonnes ajoutees ici portent l'etape courante, le bail qui empeche
-- deux workers de traiter la meme demande, le compteur de tentatives et la preuve
-- finale. Sans elles, un crash obligeait a tout recommencer, ou pire, laissait la
-- demande bloquee en Pending sans que rien ne la traite.

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'Step') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest]
        ADD [Step] NVARCHAR(40) NOT NULL
            CONSTRAINT [DF_ConversationPurgeRequest_Step] DEFAULT (N'DeleteMessageSources');
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'LeaseId') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest] ADD [LeaseId] UNIQUEIDENTIFIER NULL;
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'LeaseExpiresAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest] ADD [LeaseExpiresAt] DATETIMEOFFSET NULL;
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'AttemptCount') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest]
        ADD [AttemptCount] INT NOT NULL
            CONSTRAINT [DF_ConversationPurgeRequest_AttemptCount] DEFAULT (0);
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'NextAttemptAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest] ADD [NextAttemptAt] DATETIMEOFFSET NULL;
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'LastErrorCode') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest] ADD [LastErrorCode] NVARCHAR(100) NULL;
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'CompletedAt') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest] ADD [CompletedAt] DATETIMEOFFSET NULL;
END;

-- La preuve conserve des nombres, jamais le contenu supprime.
IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'DeletedMessageCount') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest]
        ADD [DeletedMessageCount] INT NOT NULL
            CONSTRAINT [DF_ConversationPurgeRequest_DeletedMessageCount] DEFAULT (0);
END;

IF COL_LENGTH(N'[dbo].[ConversationPurgeRequest]', N'DeletedSourceCount') IS NULL
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest]
        ADD [DeletedSourceCount] INT NOT NULL
            CONSTRAINT [DF_ConversationPurgeRequest_DeletedSourceCount] DEFAULT (0);
END;

-- Index de reclamation : un worker cherche les demandes echues, pretes a etre
-- reprises, en ignorant celles dont le bail court encore.
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_ConversationPurgeRequest_Claim'
      AND object_id = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]')
)
BEGIN
    CREATE INDEX [IX_ConversationPurgeRequest_Claim]
        ON [dbo].[ConversationPurgeRequest]([Status], [PurgeAfter], [NextAttemptAt], [LeaseExpiresAt]);
END;

-- La demande de purge portait une clef etrangere en cascade vers Conversation :
-- supprimer la conversation emportait donc la preuve de sa propre suppression.
-- La contrainte est retiree; ConversationId reste conserve comme simple
-- identifiant, ce qui permet a la preuve de survivre a la donnee purgee.
DECLARE @ForeignKeyName SYSNAME = (
    SELECT TOP 1 fk.name
    FROM sys.foreign_keys AS fk
    WHERE fk.parent_object_id = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]')
      AND EXISTS (
          SELECT 1
          FROM sys.foreign_key_columns AS fkc
          INNER JOIN sys.columns AS c
              ON c.object_id = fkc.parent_object_id
             AND c.column_id = fkc.parent_column_id
          WHERE fkc.constraint_object_id = fk.object_id
            AND c.name = N'ConversationId'
      )
);

IF @ForeignKeyName IS NOT NULL
BEGIN
    DECLARE @DropForeignKeySql NVARCHAR(MAX) =
        N'ALTER TABLE [dbo].[ConversationPurgeRequest] DROP CONSTRAINT ' + QUOTENAME(@ForeignKeyName);
    EXEC(@DropForeignKeySql);
END;
