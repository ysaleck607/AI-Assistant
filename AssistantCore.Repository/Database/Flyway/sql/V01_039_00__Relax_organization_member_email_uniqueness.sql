USE [AssistantCoreDb];

-- A guest's OrganizationMember row is keyed by (OrganizationId, IdentityProvider,
-- ExternalUserId), the Entra Object ID, never by email. A guest's client-tenant identity
-- can legitimately share an email with an unrelated internal member (their own account in
-- that same organization), and the two must remain distinct rows, never merged or blocked.
-- The (OrganizationId, IdentityProvider, ExternalUserId) unique index already guarantees
-- one row per identity; email uniqueness served no lookup and only prevented this case.
IF EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_OrganizationMember_OrganizationId_Email'
        AND object_id = OBJECT_ID(N'[dbo].[OrganizationMember]'))
BEGIN
    DROP INDEX [IX_OrganizationMember_OrganizationId_Email] ON [dbo].[OrganizationMember];
END;
