using System.Threading.Channels;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.SendMessage.Models;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;
using AssistantCore.Service.Application.Services.Messages.Authorization;
using AssistantCore.Service.Application.Services.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.Responses;
using AssistantCore.Service.Application.Services.Messages.Streaming;
using AssistantCore.Service.Application.Services.Messages.Validation;
using AssistantCore.Service.Application.Services.RateLimiting;

namespace AssistantCore.Service.Application.Commands.SendMessage;

public sealed class SendMessageStreamCommandHandler(
    ISendMessageCommandValidator validator,
    IMessageUserContextService userContextService,
    IMessageRateLimitService rateLimitService,
    IMessageProcessingLifecycleService lifecycleService,
    IAgentRuntime agentRuntime,
    ISendMessageResponseFactory responseFactory,
    IMessageStreamErrorReporter errorReporter)
    : IRequestHandler<SendMessageStreamCommand, IAsyncEnumerable<SendMessageStreamEvent>>
{
    public async Task<IAsyncEnumerable<SendMessageStreamEvent>> HandleAsync(
        SendMessageStreamCommand request,
        CancellationToken cancellationToken)
    {
        var validatedCommand = await validator.ValidateAsync(
            new SendMessageCommand(request.ConversationId, request.Message),
            cancellationToken);
        var userContext = await userContextService.GetCurrentAsync(cancellationToken);
        await rateLimitService.EnsureAllowedAsync(userContext, cancellationToken);

        var channel = Channel.CreateUnbounded<SendMessageStreamEvent>();
        _ = ProduceAsync(validatedCommand, userContext, channel.Writer, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async Task ProduceAsync(
        SendMessageCommand validatedCommand,
        MessageUserContext userContext,
        ChannelWriter<SendMessageStreamEvent> writer,
        CancellationToken cancellationToken)
    {
        StartedMessageProcessing? processing = null;

        try
        {
            processing = await lifecycleService.StartAsync(
                validatedCommand.ConversationId,
                validatedCommand.Message,
                userContext.Organization,
                userContext.Member,
                cancellationToken);
            await writer.WriteAsync(
                new SendMessageStreamEvent(
                    SendMessageStreamEvent.Accepted,
                    CreateAcceptedPayload(processing)),
                cancellationToken);

            var agentTurnResult = await agentRuntime.RunStreamingAsync(
                CreateAgentTurnRequest(processing, userContext),
                CreateStreamingCallbacks(writer),
                cancellationToken);
            var completedProcessing = await lifecycleService.CompleteAsync(
                processing,
                agentTurnResult,
                cancellationToken);
            var response = responseFactory.Create(
                processing,
                agentTurnResult,
                completedProcessing);
            await writer.WriteAsync(
                new SendMessageStreamEvent(SendMessageStreamEvent.AnswerCompleted, response),
                cancellationToken);
            writer.TryComplete();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await FailProcessingAsync(processing, wasCancelled: true);
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            var errorCode = GetErrorCode(exception);
            errorReporter.Report(
                exception,
                processing?.ConversationId ?? validatedCommand.ConversationId,
                processing?.UserMessageId,
                errorCode);
            await FailProcessingAsync(processing, wasCancelled: false);
            writer.TryWrite(new SendMessageStreamEvent(
                SendMessageStreamEvent.Error,
                new { Code = errorCode }));
            writer.TryComplete();
        }
    }

    private static AgentTurnRequest CreateAgentTurnRequest(
        StartedMessageProcessing processing,
        MessageUserContext userContext) =>
        new(
            processing,
            userContext.CreateConnectorExecutionContext());

    private static AgentTurnStreamingCallbacks CreateStreamingCallbacks(
        ChannelWriter<SendMessageStreamEvent> writer) =>
        new(
            async (message, token) => await WriteProgressEventAsync(writer, message, token),
            async (delta, token) => await WriteActivityDeltaEventAsync(writer, delta, token),
            async token => await WriteActivityCompletedEventAsync(writer, token),
            async (delta, token) => await WriteAnswerDeltaEventAsync(writer, delta, token));

    private static async ValueTask WriteProgressEventAsync(
        ChannelWriter<SendMessageStreamEvent> writer,
        string message,
        CancellationToken cancellationToken) =>
        await writer.WriteAsync(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.ProgressUpdated,
                new { Message = message }),
            cancellationToken);

    private static async ValueTask WriteAnswerDeltaEventAsync(
        ChannelWriter<SendMessageStreamEvent> writer,
        string delta,
        CancellationToken cancellationToken) =>
        await writer.WriteAsync(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.AnswerDelta,
                new { Delta = delta }),
            cancellationToken);

    private static async ValueTask WriteActivityDeltaEventAsync(
        ChannelWriter<SendMessageStreamEvent> writer,
        string delta,
        CancellationToken cancellationToken) =>
        await writer.WriteAsync(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.ActivityDelta,
                new { Delta = delta }),
            cancellationToken);

    private static async ValueTask WriteActivityCompletedEventAsync(
        ChannelWriter<SendMessageStreamEvent> writer,
        CancellationToken cancellationToken) =>
        await writer.WriteAsync(
            new SendMessageStreamEvent(
                SendMessageStreamEvent.ActivityCompleted,
                new { }),
            cancellationToken);

    /// <summary>
    /// Construit la charge utile du premier evenement. Le resume de la conversation
    /// n'y figure que lorsque l'envoi vient de la creer : le champ reste absent, et
    /// non nul, lorsque le client connait deja la conversation.
    /// </summary>
    private static object CreateAcceptedPayload(StartedMessageProcessing processing) =>
        processing.CreatedConversation is null
            ? new
            {
                processing.ConversationId,
                UserMessageId = processing.UserMessageId
            }
            : new
            {
                processing.ConversationId,
                UserMessageId = processing.UserMessageId,
                Conversation = processing.CreatedConversation
            };

    private static string GetErrorCode(Exception exception) => exception switch
    {
        AiProviderTimeoutException => "ai_provider_timeout",
        AiProviderLimitException => "ai_provider_limit",
        AiProviderUnavailableException => "ai_provider_unavailable",
        AiProviderInvalidResponseException => "ai_provider_invalid_response",
        _ => "message_generation_failed"
    };

    private async Task FailProcessingAsync(
        StartedMessageProcessing? processing,
        bool wasCancelled)
    {
        if (processing is null)
        {
            return;
        }

        await lifecycleService.FailAsync(
            processing,
            new MessageProcessingFailure(
                wasCancelled ? "message_generation_cancelled" : "message_generation_failed",
                wasCancelled),
            CancellationToken.None);
    }
}
