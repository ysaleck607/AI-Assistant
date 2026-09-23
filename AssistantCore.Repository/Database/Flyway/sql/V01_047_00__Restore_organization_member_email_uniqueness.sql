USE [AssistantCoreDb];

-- Decision d'equipe (2026-09-18) : garder l'email unique par organisation pour ne
-- pas complexifier les permissions avant le deploiement client. Le cas d'un invite
-- externe partageant un email avec un membre interne (#31/#73) est reporte a plus
-- tard - non supporte pour l'instant.
--
-- SQL Server autorise plusieurs lignes NULL dans un index unique, donc les lignes
-- pas encore rattrapees (EmailLookupHash toujours NULL) ne violent pas la contrainte.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_OrganizationMember_OrganizationId_EmailLookupHash'
        AND object_id = OBJECT_ID(N'[dbo].[OrganizationMember]')
        AND is_unique = 0)
BEGIN
    DROP INDEX [IX_OrganizationMember_OrganizationId_EmailLookupHash]
        ON [dbo].[OrganizationMember];
END;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_OrganizationMember_OrganizationId_EmailLookupHash'
        AND object_id = OBJECT_ID(N'[dbo].[OrganizationMember]'))
BEGIN
    CREATE UNIQUE INDEX [IX_OrganizationMember_OrganizationId_EmailLookupHash]
        ON [dbo].[OrganizationMember]([OrganizationId], [EmailLookupHash]);
END;
