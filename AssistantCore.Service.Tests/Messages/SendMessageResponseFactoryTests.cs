using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.Responses;

namespace AssistantCore.Service.Tests.Messages;

public sealed class SendMessageResponseFactoryTests
{
    [Theory, AutoDomainData]
    public void Given_ACompletedProcessing_When_Create_Then_MapsPersistedResponseAndSources(
        StartedMessageProcessing processing,
        CompletedMessageProcessing completedProcessing,
        DateTimeOffset occurredAt)
    {
        // Given
        var evidence = new RetrievedEvidence(
            "evidence-1",
            "SharePoint",
            "Sales report",
            "Sales increased.",
            "report-1",
            "https://example.test/report-1",
            occurredAt);
        var agentTurnResult = new AgentTurnResult(
            "Sales increased.",
            "onpremia-agent@2",
            [evidence],
            ["One source was unavailable.", "rag.groundedness.unverified", "rag.groundedness.content_rejected"],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 20, 5, 1, 1));
        var factory = new SendMessageResponseFactory();

        // When
        var response = factory.Create(
            processing,
            agentTurnResult,
            completedProcessing);

        // Then
        Assert.Equal(processing.ConversationId, response.ConversationId);
        Assert.Equal(completedProcessing.AssistantMessageId, response.MessageId);
        Assert.Equal(agentTurnResult.Content, response.Answer);
        Assert.Equal(agentTurnResult.ModelName, response.Model);
        Assert.Equal(["One source was unavailable."], response.Warnings);
        Assert.Equal(completedProcessing.CreatedAt, response.CreatedAt);
        var source = Assert.Single(response.Sources);
        Assert.Equal(evidence.SourceType, source.Type);
        Assert.Equal(evidence.Title, source.Title);
        Assert.Equal(evidence.Url, source.Url);
        Assert.Equal(evidence.Reference, source.Reference);
    }
}
