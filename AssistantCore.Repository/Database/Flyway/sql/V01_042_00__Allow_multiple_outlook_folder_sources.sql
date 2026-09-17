USE [AssistantCoreDb];

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_Microsoft365Source_Connection_Type_Resource'
      AND [object_id] = OBJECT_ID(N'[dbo].[Microsoft365Source]'))
BEGIN
    DROP INDEX [IX_Microsoft365Source_Connection_Type_Resource]
        ON [dbo].[Microsoft365Source];
END;

IF EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_Microsoft365Source_Microsoft365ConnectionId_Kind_ExternalResourceId'
      AND [object_id] = OBJECT_ID(N'[dbo].[Microsoft365Source]'))
BEGIN
    DROP INDEX [IX_Microsoft365Source_Microsoft365ConnectionId_Kind_ExternalResourceId]
        ON [dbo].[Microsoft365Source];
END;

IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE [name] = N'IX_Microsoft365Source_Connection_Type_Resource_Parent'
      AND [object_id] = OBJECT_ID(N'[dbo].[Microsoft365Source]'))
BEGIN
    CREATE UNIQUE INDEX [IX_Microsoft365Source_Connection_Type_Resource_Parent]
        ON [dbo].[Microsoft365Source]([Microsoft365ConnectionId], [Kind], [ExternalResourceId], [ParentExternalResourceId]);
END;
