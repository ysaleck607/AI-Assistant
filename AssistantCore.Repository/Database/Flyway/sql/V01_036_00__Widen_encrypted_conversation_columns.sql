USE [AssistantCoreDb];

-- Encrypted values are wider than their plaintext source (Data Protection envelope + base64),
-- so these columns must no longer be capped at their original plaintext length.

IF COL_LENGTH(N'[dbo].[Conversation]', N'Title') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[Conversation]') AND name = N'Title') <> -1
BEGIN
    ALTER TABLE [dbo].[Conversation]
        ALTER COLUMN [Title] NVARCHAR(MAX) NOT NULL;
END;

IF COL_LENGTH(N'[dbo].[MessageWarning]', N'Content') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[MessageWarning]') AND name = N'Content') <> -1
BEGIN
    ALTER TABLE [dbo].[MessageWarning]
        ALTER COLUMN [Content] NVARCHAR(MAX) NOT NULL;
END;
