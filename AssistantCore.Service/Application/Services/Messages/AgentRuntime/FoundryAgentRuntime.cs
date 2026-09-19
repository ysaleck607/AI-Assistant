using System.Diagnostics;
using System.Text.Json;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Tools;
using AssistantCore.Service.Application.Services.Messages.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Messages.AgentRuntime;

public sealed class FoundryAgentRuntime(
    IFoundryAgentClient foundryAgentClient,
    IAiToolRegistry toolRegistry,
    IAiToolCallValidator toolCallValidator,
    IToolExecutionRouter toolExecutionRouter,
    IOptions<AgentRuntimeOptions> options,
    ILogger<FoundryAgentRuntime> logger) : IAgentRuntime
{
    private const string PreparingResponseProgress = "Préparation de la réponse…";
    private static readonly string[] ToolProgressMessages =
    [
        "Je consulte les sources et contenus pertinents…",
        "J’analyse les informations disponibles…",
        "Je recoupe les données autorisées…"
    ];
    private readonly AgentRuntimeOptions _options = options.Value;

    public async Task<AgentTurnResult> RunAsync(
        AgentTurnRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CreateTurnTimeoutSource(cancellationToken);
        var context = await CreateExecutionContextAsync(request, onToolProgress: null, timeoutSource.Token);

        var response = await foundryAgentClient.RunAsync(
            context.ClientRequest,
            context.ExecuteToolAsync,
            timeoutSource.Token);

        stopwatch.Stop();
        return CreateResult(response, context.ExecutedToolResults, stopwatch.Elapsed);
    }

    public async Task<AgentTurnResult> RunStreamingAsync(
        AgentTurnRequest request,
        AgentTurnStreamingCallbacks callbacks,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(callbacks);

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CreateTurnTimeoutSource(cancellationToken);
        await callbacks.OnProgress(PreparingResponseProgress, timeoutSource.Token);
        var context = await CreateExecutionContextAsync(
            request,
            callbacks.OnProgress,
            timeoutSource.Token);

        // Tool/runtime activity from Foundry can expose implementation names such as
        // EnterpriseSearch, QueryOutlookMailbox or AnalyzeSpreadsheet. The product UI
        // receives our own generic progress messages instead.
        var response = await foundryAgentClient.RunStreamingAsync(
            context.ClientRequest,
            context.ExecuteToolAsync,
            callbacks.OnAnswerDelta,
            static (_, _) => ValueTask.CompletedTask,
            static _ => ValueTask.CompletedTask,
            timeoutSource.Token);

        stopwatch.Stop();
        return CreateResult(response, context.ExecutedToolResults, stopwatch.Elapsed);
    }

    private async Task<RuntimeExecutionContext> CreateExecutionContextAsync(
        AgentTurnRequest request,
        Func<string, CancellationToken, ValueTask>? onToolProgress,
        CancellationToken cancellationToken)
    {
        var contextStartedAt = Stopwatch.GetTimestamp();
        var availableTools = await toolRegistry.GetAvailableToolsAsync(
            request.Processing.OrganizationId,
            cancellationToken);

        var authorizedToolMappings = CanUseMicrosoft365Tools(request.ExecutionContext)
            ? CreateAuthorizedToolMappings(availableTools)
            : new Dictionary<string, AiToolDefinition>(StringComparer.Ordinal);
        var authorizedTools = authorizedToolMappings
            .Select(mapping => new FoundryAgentToolDefinition(
                mapping.Key,
                GetFoundryToolDescription(mapping.Key),
                mapping.Value.InputSchema,
                mapping.Value.Name))
            .ToArray();

        logger.LogInformation(
            "Foundry execution context prepared in {ElapsedMilliseconds} ms with {ToolCount} authorized tools: {ToolNames}.",
            Stopwatch.GetElapsedTime(contextStartedAt).TotalMilliseconds,
            authorizedTools.Length,
            string.Join(", ", authorizedTools.Select(tool => tool.Name)));

        var executedResults = new List<ToolExecutionResult>();
        var executedResultsLock = new object();
        var executionContext = CreateToolExecutionContext(request);

        async Task<string> ExecuteToolAsync(
            FoundryAgentToolCall toolCall,
            CancellationToken token)
        {
            if (!authorizedToolMappings.TryGetValue(toolCall.Name, out var internalTool))
            {
                throw new InvalidOperationException($"Foundry requested an unauthorized tool '{toolCall.Name}'.");
            }

            if (onToolProgress is not null)
            {
                await onToolProgress(CreateFriendlyToolProgress(toolCall), token);
            }

            var toolStopwatch = Stopwatch.StartNew();
            logger.LogInformation(
                "Foundry requested tool {ToolName}.",
                toolCall.Name);

            try
            {
                var requestedCall = new AiRequestedToolCall(
                    $"foundry-{Guid.NewGuid():N}",
                    internalTool.Name,
                    toolCall.Arguments);
                var validatedCall = await toolCallValidator.ValidateAsync(
                    requestedCall,
                    [internalTool],
                    token);
                var result = await toolExecutionRouter.ExecuteAsync(
                    validatedCall,
                    executionContext,
                    token);

                lock (executedResultsLock)
                {
                    executedResults.Add(result);
                }

                toolStopwatch.Stop();
                logger.LogInformation(
                    "Foundry tool {ToolName} completed in {ElapsedMilliseconds} ms with status {ToolStatus}.",
                    toolCall.Name,
                    toolStopwatch.Elapsed.TotalMilliseconds,
                    result.Status);

                return JsonSerializer.Serialize(new
                {
                    status = result.Status,
                    evidence = result.Evidence,
                    warnings = result.Warnings,
                    errorCode = result.ErrorCode
                });
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Foundry tool {ToolName} threw an unhandled exception after {ElapsedMilliseconds} ms.",
                    toolCall.Name,
                    toolStopwatch.Elapsed.TotalMilliseconds);
                throw;
            }
        }

        var clientRequest = new FoundryAgentClientRequest(
            request.Processing.ConversationHistory,
            request.Processing.UserMessage,
            authorizedTools,
            request.Processing.ConversationId);

        return new RuntimeExecutionContext(
            clientRequest,
            ExecuteToolAsync,
            executedResults);
    }

    private ConnectorExecutionContext CreateToolExecutionContext(AgentTurnRequest request) =>
        request.ExecutionContext with
        {
            RetrievalCandidateLimit = _options.RetrievalCandidateLimit,
            ConversationHistory = request.Processing.ConversationHistory
        };

    private static IReadOnlyDictionary<string, AiToolDefinition> CreateAuthorizedToolMappings(
        IReadOnlyCollection<AiToolDefinition> availableTools)
    {
        var mappings = new Dictionary<string, AiToolDefinition>(StringComparer.Ordinal);
        foreach (var tool in availableTools)
        {
            var foundryName = tool.Name switch
            {
                AiToolNames.SearchMicrosoft365 => "EnterpriseSearch",
                AiToolNames.QueryOutlookMailbox => "QueryOutlookMailbox",
                AiToolNames.AnalyzeMicrosoft365Spreadsheet => "AnalyzeSpreadsheet",
                _ => null
            };
            if (foundryName is not null)
            {
                mappings.Add(foundryName, tool);
            }
        }

        return mappings;
    }

    private static string GetFoundryToolDescription(string toolName) => toolName switch
    {
        "EnterpriseSearch" =>
            "Search authorized internal enterprise information when the answer depends on organization-specific data. "
            + "Use it for semantic research and synthesis across indexed content, including multiple emails or documents. "
            + "Use QueryOutlookMailbox instead to locate, list or read a specific current mailbox message. "
            + "When an exhaustive spreadsheet calculation is requested without an exact Excel file name, use this tool first "
            + "to identify the exact XLSX or XLSM title, then call AnalyzeSpreadsheet. Do not infer exhaustive spreadsheet "
            + "results from search excerpts.",
        "QueryOutlookMailbox" =>
            "Locate, list and read messages in the authenticated user's current Outlook mailbox. Use includeBody=true when "
            + "the answer depends on exact message content, an amount or another detail; use false when only identifying or "
            + "listing messages. It can list the newest messages without search terms. Never claim that email is unavailable "
            + "before calling this tool. Use scope received unless the user explicitly asks about sent mail or all mail. "
            + "Use limit 5 when no count is requested.",
        "AnalyzeSpreadsheet" =>
            "Use deterministic calculations over every row of an authorized Microsoft 365 XLSX or XLSM file. Always use "
            + "this tool for averages, sums, minima, maxima, counts and exhaustive row filtering; do not use semantic search "
            + "for those operations. If the exact file name is unknown, call EnterpriseSearch first to identify it, then call "
            + "this tool with that exact file name.",
        _ => throw new ArgumentOutOfRangeException(nameof(toolName), toolName, null)
    };

    private static string CreateFriendlyToolProgress(FoundryAgentToolCall toolCall)
    {
        var messages = toolCall.Name switch
        {
            "EnterpriseSearch" or "QueryOutlookMailbox" or "AnalyzeSpreadsheet" => ToolProgressMessages,
            _ => [PreparingResponseProgress]
        };

        var seed = toolCall.Arguments.GetRawText().GetHashCode(StringComparison.Ordinal);
        var index = (int)((uint)seed % (uint)messages.Length);
        return messages[index];
    }

    private static bool CanUseMicrosoft365Tools(ConnectorExecutionContext context) =>
        context.OrganizationId != Guid.Empty
        && context.MemberId != Guid.Empty
        && context.IdentityProvider == IdentityProvider.MicrosoftEntraId
        && !string.IsNullOrWhiteSpace(context.ExternalTenantId)
        && context.EntraUserId is not null
        && context.EntraUserId != Guid.Empty;

    private CancellationTokenSource CreateTurnTimeoutSource(CancellationToken cancellationToken)
    {
        var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(_options.MaximumExecutionTimeSeconds));
        return timeoutSource;
    }

    private AgentTurnResult CreateResult(
        FoundryAgentClientResult response,
        IReadOnlyCollection<ToolExecutionResult> executedToolResults,
        TimeSpan executionTime)
    {
        var allEvidence = executedToolResults
            .SelectMany(result => result.Evidence)
            .Where(evidence => !string.IsNullOrWhiteSpace(evidence.EvidenceId))
            .GroupBy(evidence => evidence.EvidenceId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var citations = EvidenceCitationSelector.Select(
            response.Content,
            allEvidence,
            _options.FinalEvidenceLimit);
        var warnings = executedToolResults
            .SelectMany(result => result.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        logger.LogInformation(
            "Foundry agent turn completed in {ElapsedMilliseconds} ms with {ModelCallCount} model calls, {ToolCallCount} tool calls, {InputTokens} input tokens and {OutputTokens} output tokens. Selected {CitationCount} exact citations from {EvidenceCount} retrieved evidence items.",
            executionTime.TotalMilliseconds,
            response.ModelCallCount,
            executedToolResults.Count,
            response.InputTokens,
            response.OutputTokens,
            citations.Count,
            allEvidence.Length);

        return new AgentTurnResult(
            response.Content,
            response.AgentIdentifier,
            citations,
            warnings,
            new AgentTurnUsage(
                executionTime,
                response.InputTokens,
                response.OutputTokens,
                response.ModelCallCount,
                executedToolResults.Count));
    }

    private sealed record RuntimeExecutionContext(
        FoundryAgentClientRequest ClientRequest,
        FoundryAgentToolExecutor ExecuteToolAsync,
        IReadOnlyCollection<ToolExecutionResult> ExecutedToolResults);
}
