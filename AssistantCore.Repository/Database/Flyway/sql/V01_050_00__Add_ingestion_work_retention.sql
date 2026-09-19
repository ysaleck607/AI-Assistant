USE [AssistantCoreDb];

SET XACT_ABORT ON;
BEGIN TRANSACTION;

-- Older permanent failures predate terminal timestamps. In pre-production, use the work
-- creation time as a conservative terminal-time backfill so retention can eventually
-- remove those rows. New failures persist their exact failedAt timestamp in CompletedAt.
IF OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]', N'U') IS NOT NULL
BEGIN
    UPDATE [dbo].[Microsoft365DocumentWork]
    SET [CompletedAt] = [CreatedAt]
    WHERE [Status] = N'PermanentFailure'
      AND [CompletedAt] IS NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]')
          AND [name] = N'IX_Microsoft365DocumentWork_TerminalRetention')
    BEGIN
        CREATE INDEX [IX_Microsoft365DocumentWork_TerminalRetention]
            ON [dbo].[Microsoft365DocumentWork] ([Status], [CompletedAt]);
    END;
END;

IF OBJECT_ID(N'[dbo].[Microsoft365ListItemWork]', N'U') IS NOT NULL
BEGIN
    UPDATE [dbo].[Microsoft365ListItemWork]
    SET [CompletedAt] = [CreatedAt]
    WHERE [Status] = N'PermanentFailure'
      AND [CompletedAt] IS NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE [object_id] = OBJECT_ID(N'[dbo].[Microsoft365ListItemWork]')
          AND [name] = N'IX_Microsoft365ListItemWork_TerminalRetention')
    BEGIN
        CREATE INDEX [IX_Microsoft365ListItemWork_TerminalRetention]
            ON [dbo].[Microsoft365ListItemWork] ([Status], [CompletedAt]);
    END;
END;

COMMIT TRANSACTION;
