USE [AssistantCoreDb];

CREATE TABLE [dbo].[OperationalIncident] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [OccurredAt] DATETIMEOFFSET NOT NULL,
    [Subsystem] NVARCHAR(50) NOT NULL,
    [Severity] NVARCHAR(50) NOT NULL,
    [CorrelationId] NVARCHAR(100) NOT NULL,
    [OrganizationId] UNIQUEIDENTIFIER NULL,
    [OrganizationMemberId] UNIQUEIDENTIFIER NULL,
    [Summary] NVARCHAR(300) NOT NULL,
    [SafeDetail] NVARCHAR(MAX) NOT NULL,
    [RelatedResourceType] NVARCHAR(50) NULL,
    [RelatedResourceId] UNIQUEIDENTIFIER NULL,
    [Status] NVARCHAR(50) NOT NULL,
    [ResolvedAt] DATETIMEOFFSET NULL,
    [ResolvedByEmail] NVARCHAR(320) NULL,
    [ResolutionNotes] NVARCHAR(MAX) NULL,
    CONSTRAINT [PK_OperationalIncident] PRIMARY KEY CLUSTERED ([Id]),
    CONSTRAINT [FK_OperationalIncident_Organization] FOREIGN KEY ([OrganizationId])
        REFERENCES [dbo].[Organization] ([Id])
);

CREATE INDEX [IX_OperationalIncident_OrganizationId_OccurredAt]
    ON [dbo].[OperationalIncident] ([OrganizationId], [OccurredAt]);

CREATE INDEX [IX_OperationalIncident_CorrelationId]
    ON [dbo].[OperationalIncident] ([CorrelationId]);

CREATE INDEX [IX_OperationalIncident_Subsystem_Severity_OccurredAt]
    ON [dbo].[OperationalIncident] ([Subsystem], [Severity], [OccurredAt]);

CREATE INDEX [IX_OperationalIncident_RelatedResourceType_RelatedResourceId]
    ON [dbo].[OperationalIncident] ([RelatedResourceType], [RelatedResourceId]);
