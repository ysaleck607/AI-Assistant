USE [AssistantCoreDb];

-- Encrypted values are wider than their plaintext source (Data Protection envelope + base64),
-- so these columns must no longer be capped at their original plaintext length.

IF COL_LENGTH(N'[dbo].[Microsoft365ListItemWork]', N'WebUrl') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365ListItemWork]') AND name = N'WebUrl') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365ListItemWork]
        ALTER COLUMN [WebUrl] NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365Source]', N'DisplayName') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365Source]') AND name = N'DisplayName') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365Source]
        ALTER COLUMN [DisplayName] NVARCHAR(MAX) NOT NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365Source]', N'WebUrl') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365Source]') AND name = N'WebUrl') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365Source]
        ALTER COLUMN [WebUrl] NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365DocumentWork]', N'Name') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]') AND name = N'Name') <> -1
BEGIN
    -- CK_Microsoft365DocumentWork_Payload depends on [Name]; SQL Server refuses to widen a
    -- column referenced by a CHECK constraint, so drop and recreate it around the ALTER.
    IF EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_Microsoft365DocumentWork_Payload'
            AND parent_object_id = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]'))
    BEGIN
        ALTER TABLE [dbo].[Microsoft365DocumentWork]
            DROP CONSTRAINT [CK_Microsoft365DocumentWork_Payload];
    END;

    ALTER TABLE [dbo].[Microsoft365DocumentWork]
        ALTER COLUMN [Name] NVARCHAR(MAX) NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_Microsoft365DocumentWork_Payload'
            AND parent_object_id = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]'))
    BEGIN
        ALTER TABLE [dbo].[Microsoft365DocumentWork]
            ADD CONSTRAINT [CK_Microsoft365DocumentWork_Payload]
                CHECK (
                    ([WorkType] = N'ProcessDocument' AND [Name] IS NOT NULL AND [ETag] IS NOT NULL)
                    OR
                    ([WorkType] = N'DeleteDocument' AND [Name] IS NULL AND [ETag] IS NULL AND [Size] IS NULL AND [MimeType] IS NULL)
                );
    END;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365DocumentWork]', N'WebUrl') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365DocumentWork]') AND name = N'WebUrl') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365DocumentWork]
        ALTER COLUMN [WebUrl] NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365IndexedContent]', N'SiteUrl') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365IndexedContent]') AND name = N'SiteUrl') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365IndexedContent]
        ALTER COLUMN [SiteUrl] NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365IndexedContent]', N'Title') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365IndexedContent]') AND name = N'Title') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365IndexedContent]
        ALTER COLUMN [Title] NVARCHAR(MAX) NULL;
END;

IF COL_LENGTH(N'[dbo].[Microsoft365IndexedContent]', N'WebUrl') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Microsoft365IndexedContent]') AND name = N'WebUrl') <> -1
BEGIN
    ALTER TABLE [dbo].[Microsoft365IndexedContent]
        ALTER COLUMN [WebUrl] NVARCHAR(MAX) NULL;
END;
