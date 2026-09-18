using AssistantCore.Service.Application.Commands.SendMessage;
using AssistantCore.Service.Application.Commands.SendMessage.Models;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.AiModels;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;
using AssistantCore.Service.Application.Services.Messages.Authorization;
using AssistantCore.Service.Application.Services.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.Responses;
using AssistantCore.Service.Application.Services.Messages.Streaming;
using AssistantCore.Service.Application.Services.Messages.Validation;
using AssistantCore.Service.Application.Services.RateLimiting;

namespace AssistantCore.Service.Tests.Messages;

public sealed class SendMessageCommandHandlerTests
{
    [Theory, AutoDomainData]
    public async Task Given_AValidCommand_When_HandleAsync_Then_OrchestratesInRequiredOrder(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AiConversationMessage historyMessage,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse expectedResponse)
    {
        // Given
        processing = processing with { ConversationHistory = [historyMessage] };
        var operations = new List<string>();
        var lifecycle = new StubLifecycleService(operations, processing, completedProcessing);
        var agentRuntime = new StubAgentRuntime(operations, agentTurnResult);
        var handler = new SendMessageCommandHandler(
            new StubCommandValidator(operations),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations),
            lifecycle,
            agentRuntime,
            new StubResponseFactory(operations, expectedResponse));

        // When
        var result = await handler.HandleAsync(command, CancellationToken.None);

        // Then
        Assert.Same(expectedResponse, result);
        Assert.Equal(
            [
                "Validate",
                "ResolveUser",
                "RateLimit",
                "StartProcessing",
                "RunAgent",
                "CompleteProcessing",
                "BuildResponse"
            ],
            operations);
        Assert.Equal(
            [historyMessage],
            agentRuntime.ReceivedRequest!.Processing.ConversationHistory);
        Assert.Equal(
            userContext.Organization.Id,
            agentRuntime.ReceivedRequest.ExecutionContext.OrganizationId);
        Assert.Same(agentTurnResult, lifecycle.ReceivedAgentTurnResult);
    }

    [Theory, AutoDomainData]
    public async Task Given_AConversationHistory_When_HandleAsyncStreaming_Then_PassesItToTheAgent(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AiConversationMessage historyMessage,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        processing = processing with { ConversationHistory = [historyMessage] };
        var operations = new List<string>();
        var agentRuntime = new StubAgentRuntime(operations, agentTurnResult);
        var handler = CreateStreamHandler(
            operations,
            userContext,
            processing,
            completedProcessing,
            agentRuntime,
            response);

        // When
        var events = await handler.HandleAsync(
            new SendMessageStreamCommand(command.ConversationId, command.Message),
            CancellationToken.None);
        await foreach (var _ in events)
        {
        }

        // Then
        Assert.Equal(
            [historyMessage],
            agentRuntime.ReceivedRequest!.Processing.ConversationHistory);
        Assert.Equal(
            userContext.Member.Id,
            agentRuntime.ReceivedRequest.ExecutionContext.MemberId);
    }

    [Theory, AutoDomainData]
    public async Task Given_AProgressUpdate_When_HandleAsyncStreaming_Then_ReturnsADedicatedProgressEvent(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        const string progressMessage = "Je consulte les documents pertinents.";
        var operations = new List<string>();
        var handler = CreateStreamHandler(
            operations,
            userContext,
            processing,
            completedProcessing,
            new StubAgentRuntime(
                operations,
                agentTurnResult,
                progressMessage: progressMessage),
            response);

        // When
        var events = await handler.HandleAsync(
            new SendMessageStreamCommand(command.ConversationId, command.Message),
            CancellationToken.None);
        var receivedEvents = await ReadAllAsync(events);

        // Then
        var progressEvent = Assert.Single(receivedEvents, streamEvent =>
            streamEvent.Name == SendMessageStreamEvent.ProgressUpdated);
        Assert.Equal(
            progressMessage,
            progressEvent.Data.GetType().GetProperty("Message")?.GetValue(progressEvent.Data));
    }

    [Theory, AutoDomainData]
    public async Task Given_AnActivityUpdate_When_HandleAsyncStreaming_Then_ReturnsActivityEvents(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        const string activityMessage = "Recherche dans les informations autorisées…";
        var handler = CreateStreamHandler(
            [],
            userContext,
            processing,
            completedProcessing,
            new StubAgentRuntime(
                [],
                agentTurnResult,
                activityMessage: activityMessage),
            response);

        // When
        var events = await handler.HandleAsync(
            new SendMessageStreamCommand(command.ConversationId, command.Message),
            CancellationToken.None);
        var receivedEvents = await ReadAllAsync(events);

        // Then
        var activityEvent = Assert.Single(receivedEvents, streamEvent =>
            streamEvent.Name == SendMessageStreamEvent.ActivityDelta);
        Assert.Equal(
            activityMessage,
            activityEvent.Data.GetType().GetProperty("Delta")?.GetValue(activityEvent.Data));
        Assert.Contains(
            receivedEvents,
            streamEvent => streamEvent.Name == SendMessageStreamEvent.ActivityCompleted);
    }

    [Theory, AutoDomainData]
    public async Task Given_AProviderTimeout_When_HandleAsyncStreaming_Then_ReturnsTheTimeoutErrorCode(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        var operations = new List<string>();
        var lifecycle = new StubLifecycleService(operations, processing, completedProcessing);
        var errorReporter = new StubMessageStreamErrorReporter();
        var handler = new SendMessageStreamCommandHandler(
            new StubCommandValidator(operations),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations),
            lifecycle,
            new StubAgentRuntime(
                operations,
                agentTurnResult,
                new AiProviderTimeoutException("Foundry")),
            new StubResponseFactory(operations, response),
            errorReporter);

        // When
        var events = await handler.HandleAsync(
            new SendMessageStreamCommand(command.ConversationId, command.Message),
            CancellationToken.None);
        var receivedEvents = await ReadAllAsync(events);

        // Then
        var errorEvent = Assert.Single(receivedEvents, streamEvent =>
            streamEvent.Name == SendMessageStreamEvent.Error);
        Assert.Equal(
            "ai_provider_timeout",
            errorEvent.Data.GetType().GetProperty("Code")?.GetValue(errorEvent.Data));
        Assert.False(lifecycle.ReceivedFailure?.WasCancelled);
        Assert.IsType<AiProviderTimeoutException>(errorReporter.ReceivedException);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidCommand_When_HandleAsync_Then_StopsBeforeResolvingUser(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        var operations = new List<string>();
        var expectedException = new BadRequestException("Invalid message.");
        var handler = new SendMessageCommandHandler(
            new StubCommandValidator(operations, expectedException),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations),
            new StubLifecycleService(operations, processing, completedProcessing),
            new StubAgentRuntime(operations, agentTurnResult),
            new StubResponseFactory(operations, response));

        // When
        var exception = await Record.ExceptionAsync(() =>
            handler.HandleAsync(command, CancellationToken.None));

        // Then
        Assert.Same(expectedException, exception);
        Assert.Equal(["Validate"], operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARateLimitedMember_When_HandleAsync_Then_StopsBeforeStartingProcessing(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        var operations = new List<string>();
        var expectedException = new RequestRateLimitExceededException(30);
        var handler = new SendMessageCommandHandler(
            new StubCommandValidator(operations),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations, expectedException),
            new StubLifecycleService(operations, processing, completedProcessing),
            new StubAgentRuntime(operations, agentTurnResult),
            new StubResponseFactory(operations, response));

        // When
        var exception = await Record.ExceptionAsync(() =>
            handler.HandleAsync(command, CancellationToken.None));

        // Then
        Assert.Same(expectedException, exception);
        Assert.Equal(["Validate", "ResolveUser", "RateLimit"], operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnAgentFailure_When_HandleAsync_Then_FailsStartedProcessing(
        SendMessageCommand command,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        CompletedMessageProcessing completedProcessing,
        SendMessageResponse response)
    {
        // Given
        var operations = new List<string>();
        var expectedException = new InvalidOperationException("Foundry is unavailable.");
        var lifecycle = new StubLifecycleService(operations, processing, completedProcessing);
        var handler = new SendMessageCommandHandler(
            new StubCommandValidator(operations),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations),
            lifecycle,
            new StubAgentRuntime(operations, agentTurnResult, expectedException),
            new StubResponseFactory(operations, response));

        // When
        var exception = await Record.ExceptionAsync(() =>
            handler.HandleAsync(command, CancellationToken.None));

        // Then
        Assert.Same(expectedException, exception);
        Assert.Equal(
            ["Validate", "ResolveUser", "RateLimit", "StartProcessing", "RunAgent"],
            operations);
        Assert.Equal("message_generation_failed", lifecycle.ReceivedFailure?.ErrorCode);
    }

    private static SendMessageStreamCommandHandler CreateStreamHandler(
        List<string> operations,
        MessageUserContext userContext,
        StartedMessageProcessing processing,
        CompletedMessageProcessing completedProcessing,
        IAgentRuntime agentRuntime,
        SendMessageResponse response) =>
        new(
            new StubCommandValidator(operations),
            new StubUserContextService(operations, userContext),
            new StubMessageRateLimitService(operations),
            new StubLifecycleService(operations, processing, completedProcessing),
            agentRuntime,
            new StubResponseFactory(operations, response),
            new StubMessageStreamErrorReporter());

    private static async Task<List<SendMessageStreamEvent>> ReadAllAsync(
        IAsyncEnumerable<SendMessageStreamEvent> events)
    {
        var receivedEvents = new List<SendMessageStreamEvent>();
        await foreach (var streamEvent in events)
        {
            receivedEvents.Add(streamEvent);
        }

        return receivedEvents;
    }

    private sealed class StubCommandValidator(
        List<string> operations,
        Exception? exception = null) : ISendMessageCommandValidator
    {
        public Task<SendMessageCommand> ValidateAsync(
            SendMessageCommand command,
            CancellationToken cancellationToken)
        {
            operations.Add("Validate");
            return exception is null
                ? Task.FromResult(command)
                : Task.FromException<SendMessageCommand>(exception);
        }
    }

    private sealed class StubUserContextService(
        List<string> operations,
        MessageUserContext context) : IMessageUserContextService
    {
        public Task<MessageUserContext> GetCurrentAsync(CancellationToken cancellationToken)
        {
            operations.Add("ResolveUser");
            return Task.FromResult(context);
        }
    }

    private sealed class StubMessageRateLimitService(
        List<string> operations,
        Exception? exception = null) : IMessageRateLimitService
    {
        public Task EnsureAllowedAsync(
            MessageUserContext userContext,
            CancellationToken cancellationToken)
        {
            operations.Add("RateLimit");
            return exception is null
                ? Task.CompletedTask
                : Task.FromException(exception);
        }
    }

    private sealed class StubLifecycleService(
        List<string> operations,
        StartedMessageProcessing processing,
        CompletedMessageProcessing completedProcessing)
        : IMessageProcessingLifecycleService
    {
        public AgentTurnResult? ReceivedAgentTurnResult { get; private set; }

        public MessageProcessingFailure? ReceivedFailure { get; private set; }

        public Task<StartedMessageProcessing> StartAsync(
            Guid? conversationId,
            string message,
            AssistantCore.Repository.Domain.Entities.Organization organization,
            AssistantCore.Repository.Domain.Entities.OrganizationMember member,
            CancellationToken cancellationToken)
        {
            operations.Add("StartProcessing");
            return Task.FromResult(processing);
        }

        public Task<CompletedMessageProcessing> CompleteAsync(
            StartedMessageProcessing startedProcessing,
            AgentTurnResult result,
            CancellationToken cancellationToken)
        {
            operations.Add("CompleteProcessing");
            ReceivedAgentTurnResult = result;
            return Task.FromResult(completedProcessing);
        }

        public Task MarkAsInProgressAsync(
            StartedMessageProcessing startedProcessing,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task FailAsync(
            StartedMessageProcessing startedProcessing,
            MessageProcessingFailure failure,
            CancellationToken cancellationToken)
        {
            ReceivedFailure = failure;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAgentRuntime(
        List<string> operations,
        AgentTurnResult result,
        Exception? exception = null,
        string? progressMessage = null,
        string? activityMessage = null) : IAgentRuntime
    {
        public AgentTurnRequest? ReceivedRequest { get; private set; }

        public Task<AgentTurnResult> RunAsync(
            AgentTurnRequest request,
            CancellationToken cancellationToken)
        {
            operations.Add("RunAgent");
            ReceivedRequest = request;
            return exception is null
                ? Task.FromResult(result)
                : Task.FromException<AgentTurnResult>(exception);
        }

        public async Task<AgentTurnResult> RunStreamingAsync(
            AgentTurnRequest request,
            AgentTurnStreamingCallbacks callbacks,
            CancellationToken cancellationToken)
        {
            operations.Add("RunAgent");
            ReceivedRequest = request;
            if (progressMessage is not null)
            {
                await callbacks.OnProgress(progressMessage, cancellationToken);
            }
            if (activityMessage is not null)
            {
                await callbacks.OnActivityDelta(activityMessage, cancellationToken);
                await callbacks.OnActivityCompleted(cancellationToken);
            }

            return exception is null
                ? result
                : await Task.FromException<AgentTurnResult>(exception);
        }
    }

    private sealed class StubMessageStreamErrorReporter : IMessageStreamErrorReporter
    {
        public Exception? ReceivedException { get; private set; }

        public void Report(
            Exception exception,
            Guid? conversationId,
            Guid? userMessageId,
            string errorCode) =>
            ReceivedException = exception;
    }

    private sealed class StubResponseFactory(
        List<string> operations,
        SendMessageResponse response) : ISendMessageResponseFactory
    {
        public SendMessageResponse Create(
            StartedMessageProcessing processing,
            AgentTurnResult agentTurnResult,
            CompletedMessageProcessing completedProcessing)
        {
            operations.Add("BuildResponse");
            return response;
        }
    }
}
