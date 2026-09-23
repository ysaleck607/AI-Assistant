using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages.Tools;

namespace AssistantCore.Service.Application.Services.Messages.Tools;

public sealed class AiToolCallValidator(
    IAiToolArgumentSchemaValidator schemaValidator,
    IAiToolArgumentSecurityValidator securityValidator,
    IAiToolDateRangeValidator dateRangeValidator) : IAiToolCallValidator
{
    private const int MaximumCallIdLength = 100;

    private static readonly HashSet<string> ReadOnlyToolNames =
        [
            AiToolNames.SearchMicrosoft365,
            AiToolNames.QueryOutlookMailbox,
            AiToolNames.AnalyzeMicrosoft365Spreadsheet
        ];

    public Task<ValidatedToolCall> ValidateAsync(
        AiRequestedToolCall requestedToolCall,
        IReadOnlyCollection<AiToolDefinition> availableTools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ValidateCallId(requestedToolCall.CallId);
        var availableTool = FindAvailableTool(requestedToolCall, availableTools);
        ValidateReadOnlyTool(requestedToolCall);

        securityValidator.Validate(
            requestedToolCall.Arguments,
            requestedToolCall.CallId);
        schemaValidator.Validate(
            requestedToolCall.Arguments,
            availableTool.InputSchema,
            requestedToolCall.CallId);
        dateRangeValidator.Validate(
            requestedToolCall.Arguments,
            requestedToolCall.CallId);

        return Task.FromResult(new ValidatedToolCall(
            requestedToolCall.CallId,
            requestedToolCall.ToolName,
            requestedToolCall.Arguments.Clone()));
    }

    private static void ValidateCallId(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId) || callId.Length > MaximumCallIdLength)
        {
            throw new ToolCallValidationException(
                callId,
                $"The tool call identifier must contain between 1 and {MaximumCallIdLength} characters.");
        }
    }

    private static AiToolDefinition FindAvailableTool(
        AiRequestedToolCall requestedToolCall,
        IReadOnlyCollection<AiToolDefinition> availableTools)
    {
        var matches = availableTools
            .Where(tool => string.Equals(
                tool.Name,
                requestedToolCall.ToolName,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        if (matches.Length != 1)
        {
            throw new ToolCallValidationException(
                requestedToolCall.CallId,
                "The requested tool is not uniquely available in the current registry.");
        }

        return matches[0];
    }

    private static void ValidateReadOnlyTool(AiRequestedToolCall requestedToolCall)
    {
        if (!ReadOnlyToolNames.Contains(requestedToolCall.ToolName))
        {
            throw new ToolCallValidationException(
                requestedToolCall.CallId,
                "The requested tool is not an approved read-only operation.");
        }
    }
}
