USE [AssistantCoreDb];

IF OBJECT_ID(N'[dbo].[CK_Microsoft365Source_Kind]', N'C') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[Microsoft365Source]
        DROP CONSTRAINT [CK_Microsoft365Source_Kind];
END;

ALTER TABLE [dbo].[Microsoft365Source]
    ADD CONSTRAINT [CK_Microsoft365Source_Kind]
        CHECK ([Kind] IN (N'SharePointSite', N'SharePointDrive', N'SharePointList', N'OneDrive', N'OutlookMailbox'));

IF OBJECT_ID(N'[dbo].[CK_OrganizationConnectorSource_SourceType]', N'C') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[OrganizationConnectorSource]
        DROP CONSTRAINT [CK_OrganizationConnectorSource_SourceType];
END;

ALTER TABLE [dbo].[OrganizationConnectorSource]
    ADD CONSTRAINT [CK_OrganizationConnectorSource_SourceType]
        CHECK ([SourceType] IN (N'SharePoint', N'OneDrive', N'Outlook'));
