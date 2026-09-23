using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;

namespace AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;

public interface IMicrosoft365OutlookMailboxQuery
{
    Task<ConnectorResult> QueryAsync(
        QueryOutlookMailboxToolArguments request,
        ConnectorExecutionContext context,
        CancellationToken cancellationToken);
}
