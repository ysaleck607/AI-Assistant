using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Commands.SendMessage.Models;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;
using AssistantCore.Service.Application.Services.Messages.Authorization;
using AssistantCore.Service.Application.Services.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Messages.Responses;
using AssistantCore.Service.Application.Services.Messages.Validation;
using AssistantCore.Service.Application.Services.RateLimiting;
using System.Diagnostics;

namespace AssistantCore.Service.Application.Commands.SendMessage;

public sealed class SendMessageCommandHandler(
    ISendMessageCommandValidator validator,
    IMessageUserContextService userContextService,
    IMessageRateLimitService rateLimitService,
    IMessageProcessingLifecycleService lifecycleService,
    IAgentRuntime agentRuntime,
    ISendMessageResponseFactory responseFactory)
    : IRequestHandler<SendMessageCommand, SendMessageResponse>
{
    public async Task<SendMessageResponse> HandleAsync(
        SendMessageCommand request,
        CancellationToken cancellationToken)
    {
        var startedAt = Stopwatch.GetTimestamp();
        using var activity = MessageTelemetry.Activities.StartActivity("messages.process");
        activity?.SetTag("messages.operation", "send");
        StartedMessageProcessing? processing = null;

        try
        {
            var validatedCommand = await validator.ValidateAsync(request, cancellationToken);
            var userContext = await userContextService.GetCurrentAsync(cancellationToken);
            await rateLimitService.EnsureAllowedAsync(userContext, cancellationToken);
            processing = await lifecycleService.StartAsync(
                validatedCommand.ConversationId,
                validatedCommand.Message,
                userContext.Organization,
                userContext.Member,
                cancellationToken);
            var agentTurnResult = await agentRuntime.RunAsync(
                new AgentTurnRequest(
                    processing,
                    userContext.CreateConnectorExecutionContext()),
                cancellationToken);
            var completedProcessing = await lifecycleService.CompleteAsync(
                processing,
                agentTurnResult,
                cancellationToken);

            activity?.SetTag("messages.outcome", "completed");
            return responseFactory.Create(
                processing,
                agentTurnResult,
                completedProcessing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            activity?.SetTag("messages.outcome", "cancelled");
            await FailProcessingAsync(processing, wasCancelled: true);
            throw;
        }
        catch
        {
            activity?.SetTag("messages.outcome", "failed");
            await FailProcessingAsync(processing, wasCancelled: false);
            throw;
        }
        finally
        {
            MessageTelemetry.RecordDuration(
                "messages.process.duration_ms",
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
    }

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
