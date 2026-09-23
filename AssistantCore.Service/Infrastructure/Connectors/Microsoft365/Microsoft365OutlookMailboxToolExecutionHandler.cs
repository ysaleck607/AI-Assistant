using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Tools;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Messages.Tools;
using Microsoft.Extensions.Logging;

namespace AssistantCore.Service.Infrastructure.Connectors.Microsoft365;

public sealed class Microsoft365OutlookMailboxToolExecutionHandler(
    IMicrosoft365OutlookMailboxQuery mailboxQuery,
    ILogger<Microsoft365OutlookMailboxToolExecutionHandler> logger)
    : AiToolExecutionHandler<QueryOutlookMailboxToolArguments>(AiToolNames.QueryOutlookMailbox)
{
    protected override async Task<ToolExecutionResult> ExecuteAsync(
        string toolCallId,
        QueryOutlookMailboxToolArguments arguments,
        ConnectorExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await mailboxQuery.QueryAsync(arguments, executionContext, cancellationToken);
            return ToolExecutionResult.Succeeded(toolCallId, result.Evidence);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolExecutionResult.Failed(
                toolCallId,
                ToolExecutionErrorCodes.OutlookMailboxTimeout,
                ["The Outlook mailbox query timed out."]);
        }
        catch (Microsoft365ExternalException exception)
        {
            logger.LogError(exception, "The Outlook mailbox query failed.");
            return ToolExecutionResult.Failed(
                toolCallId,
                ToolExecutionErrorCodes.OutlookMailboxUnavailable,
                ["The Outlook mailbox could not be consulted."]);
        }
    }
}
