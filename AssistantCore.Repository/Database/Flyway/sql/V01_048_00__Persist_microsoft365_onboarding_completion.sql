USE [AssistantCoreDb];

ALTER TABLE [dbo].[Microsoft365Connection]
ADD [OnboardingCompletedAt] DATETIMEOFFSET NULL;

-- Preserve organizations that already completed setup before this marker existed.
-- A discovered SharePoint site proves that the initial source-selection step was reached.
EXEC sys.sp_executesql N'
UPDATE connection
SET [OnboardingCompletedAt] = COALESCE(connection.[ConsentValidatedAt], connection.[UpdatedAt])
FROM [dbo].[Microsoft365Connection] connection
WHERE connection.[Status] = ''Active''
  AND connection.[OnboardingCompletedAt] IS NULL
  AND EXISTS (
      SELECT 1
      FROM [dbo].[Microsoft365Site] site
      WHERE site.[OrganizationId] = connection.[OrganizationId]
  );';
