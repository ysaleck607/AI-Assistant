USE [AssistantCoreDb];

-- Outlook subscriptions created before this migration listened only to
-- "updated" notifications. New messages emit "created" notifications, so
-- make the Worker recreate those subscriptions with the corrected change types.
UPDATE subscription
SET
    [MicrosoftSubscriptionId] = NULL,
    [ProtectedClientState] = NULL,
    [ExpiresAt] = NULL,
    [Status] = N'Pending',
    [LastErrorCode] = NULL,
    [UpdatedAt] = TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')
FROM [dbo].[Microsoft365Subscription] subscription
INNER JOIN [dbo].[Microsoft365Source] source
    ON source.[Id] = subscription.[Microsoft365SourceId]
WHERE source.[Kind] = N'OutlookMailbox'
  AND subscription.[MicrosoftSubscriptionId] IS NOT NULL;
