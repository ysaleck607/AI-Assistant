using System.Text.Json;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Tools;
using AssistantCore.Service.Application.Models.Messages.Tools.Arguments;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Infrastructure.Connectors.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365OutlookMailboxToolExecutionHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_AValidMailboxQuery_When_ExecuteAsync_Then_ReturnsMailboxEvidence(
        string toolCallId)
    {
        // Given
        var evidence = new RetrievedEvidence(
            "evidence-1",
            "Microsoft365",
            "Réponse clientèle",
            "Expéditeur : Giovani",
            "outlook:message-1",
            "https://outlook.office.com/mail/message-1",
            new DateTimeOffset(2026, 9, 16, 21, 55, 0, TimeSpan.Zero));
        var mailboxQuery = new StubMailboxQuery(new ConnectorResult([evidence]));
        var handler = new Microsoft365OutlookMailboxToolExecutionHandler(
            mailboxQuery,
            NullLogger<Microsoft365OutlookMailboxToolExecutionHandler>.Instance);
        var arguments = new QueryOutlookMailboxToolArguments(
            Query: null,
            Sender: "Giovani",
            Recipient: null,
            Scope: "received",
            DateFrom: null,
            DateTo: null,
            IncludeBody: true,
            Limit: 5);
        var toolCall = new ValidatedToolCall(
            toolCallId,
            AiToolNames.QueryOutlookMailbox,
            JsonSerializer.SerializeToElement(arguments, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        // When
        var result = await handler.ExecuteAsync(
            toolCall,
            new ConnectorExecutionContext(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        // Then
        Assert.Equal(ToolExecutionStatus.Success, result.Status);
        Assert.Equal(evidence, Assert.Single(result.Evidence));
        Assert.Equal(arguments, mailboxQuery.ReceivedRequest);
    }

    private sealed class StubMailboxQuery(ConnectorResult result) : IMicrosoft365OutlookMailboxQuery
    {
        public QueryOutlookMailboxToolArguments? ReceivedRequest { get; private set; }

        public Task<ConnectorResult> QueryAsync(
            QueryOutlookMailboxToolArguments request,
            ConnectorExecutionContext context,
            CancellationToken cancellationToken)
        {
            ReceivedRequest = request;
            return Task.FromResult(result);
        }
    }
}
