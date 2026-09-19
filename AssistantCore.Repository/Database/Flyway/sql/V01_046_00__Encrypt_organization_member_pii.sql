USE [AssistantCoreDb];

-- Name and Email will hold applicative ciphertext, which is wider than the plaintext
-- they replace.
IF COL_LENGTH(N'[dbo].[OrganizationMember]', N'Name') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[OrganizationMember]') AND name = N'Name') <> -1
BEGIN
    ALTER TABLE [dbo].[OrganizationMember]
        ALTER COLUMN [Name] NVARCHAR(MAX) NOT NULL;
END;

IF COL_LENGTH(N'[dbo].[OrganizationMember]', N'Email') IS NOT NULL
    AND (SELECT max_length FROM sys.columns
         WHERE object_id = OBJECT_ID(N'[dbo].[OrganizationMember]') AND name = N'Email') <> -1
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'IX_OrganizationMember_OrganizationId_Email'
            AND object_id = OBJECT_ID(N'[dbo].[OrganizationMember]'))
    BEGIN
        DROP INDEX [IX_OrganizationMember_OrganizationId_Email]
            ON [dbo].[OrganizationMember];
    END;

    ALTER TABLE [dbo].[OrganizationMember]
        ALTER COLUMN [Email] NVARCHAR(MAX) NOT NULL;
END;

-- Blind index for exact-match lookups on the now-encrypted email. Nullable: existing
-- rows only get one once the reencryption backfill runs against them; never unique -
-- two distinct external identities may legitimately share an email (see #31).
IF COL_LENGTH(N'[dbo].[OrganizationMember]', N'EmailLookupHash') IS NULL
BEGIN
    ALTER TABLE [dbo].[OrganizationMember]
        ADD [EmailLookupHash] NVARCHAR(64) NULL;
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_OrganizationMember_OrganizationId_EmailLookupHash'
        AND object_id = OBJECT_ID(N'[dbo].[OrganizationMember]'))
BEGIN
    CREATE INDEX [IX_OrganizationMember_OrganizationId_EmailLookupHash]
        ON [dbo].[OrganizationMember]([OrganizationId], [EmailLookupHash]);
END;
