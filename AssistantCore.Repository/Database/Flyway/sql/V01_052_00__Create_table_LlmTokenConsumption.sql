USE [AssistantCoreDb];

CREATE TABLE [dbo].[LlmTokenConsumption] (
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [Model] NVARCHAR(100) NOT NULL,
    [PeriodStart] DATETIMEOFFSET NOT NULL,
    [TokensConsumed] BIGINT NOT NULL,
    [UpdatedAt] DATETIMEOFFSET NOT NULL,
    CONSTRAINT [PK_LlmTokenConsumption] PRIMARY KEY CLUSTERED ([Id])
);

CREATE UNIQUE INDEX [IX_LlmTokenConsumption_Model_PeriodStart]
    ON [dbo].[LlmTokenConsumption] ([Model], [PeriodStart]);
