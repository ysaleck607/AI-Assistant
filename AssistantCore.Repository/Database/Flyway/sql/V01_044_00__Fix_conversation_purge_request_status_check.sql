USE [AssistantCoreDb];

-- CK_ConversationPurgeRequest_Status only ever allowed 'Pending'/'Completed', from
-- before the resumable purge rework added Processing/TemporaryFailure/PermanentFailure
-- to ConversationPurgeStatus. Every write of one of those three values - the very
-- first thing ClaimNextAsync does - would be rejected by SQL Server with a CHECK
-- violation. Recreate the constraint with the full set of statuses.
IF EXISTS (
    SELECT 1 FROM sys.check_constraints
    WHERE name = N'CK_ConversationPurgeRequest_Status'
        AND parent_object_id = OBJECT_ID(N'[dbo].[ConversationPurgeRequest]'))
BEGIN
    ALTER TABLE [dbo].[ConversationPurgeRequest]
        DROP CONSTRAINT [CK_ConversationPurgeRequest_Status];
END;

ALTER TABLE [dbo].[ConversationPurgeRequest]
    ADD CONSTRAINT [CK_ConversationPurgeRequest_Status]
        CHECK ([Status] IN (
            N'Pending',
            N'Processing',
            N'TemporaryFailure',
            N'PermanentFailure',
            N'Completed'));
