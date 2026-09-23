using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text.Json;
using AssistantCore.ExternalServices.Entities.Foundry;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AssistantCore.ExternalServices.Services.Foundry;

public sealed class FoundryAgentExternalClient
{
    private const string CurrentTurnInstruction =
        "Treat the current user message as the authoritative intent for this turn. "
        + "Use previous conversation context when the current message is an explicit follow-up, contains a pronoun or reference that needs resolution, compares with earlier information, or clearly asks to continue the previous topic. "
        + "When the current message is self-contained or introduces a different subject, answer only that new subject and do not repeat, merge, or carry unrelated facts from the previous answer.";

    private readonly FoundryAgentClientSettings _settings;
    private readonly AIProjectClient _projectClient;
    private readonly ILogger<FoundryAgentExternalClient> _logger;
    private readonly SemaphoreSlim _configurationValidationLock = new(1, 1);
    private volatile bool _configurationValidated;

    public FoundryAgentExternalClient(
        FoundryAgentClientSettings settings,
        ILogger<FoundryAgentExternalClient> logger)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _logger = logger;
        _projectClient = new AIProjectClient(
            new Uri(settings.ProjectEndpoint),
            new DefaultAzureCredential());
    }

    public async Task<FoundryAgentExternalResult> RunAsync(
        FoundryAgentExternalRequest request,
        Func<FoundryAgentExternalToolCall, CancellationToken, Task<string>> toolExecutor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolExecutor);

        await ValidateConfigurationOnceAsync(cancellationToken);
        var agent = CreateAgent(request.Tools, toolExecutor);

        // Conversation context is intentionally supplied by the application on every turn.
        // We do not retain an unbounded in-memory Foundry AgentSession because doing so would
        // make behavior depend on process lifetime and silently bypass the application's
        // bounded conversation-history policy.
        var response = await agent.RunAsync(
            CreateMessages(request),
            cancellationToken: cancellationToken);

        return new FoundryAgentExternalResult(
            response.Text,
            CreateAgentIdentifier(),
            ToTokenCount(response.Usage?.InputTokenCount),
            ToTokenCount(response.Usage?.OutputTokenCount),
            ModelCallCount: 1);
    }

    public async Task<FoundryAgentExternalResult> RunStreamingAsync(
        FoundryAgentExternalRequest request,
        Func<FoundryAgentExternalToolCall, CancellationToken, Task<string>> toolExecutor,
        Func<string, CancellationToken, ValueTask> onAnswerDelta,
        Func<string, CancellationToken, ValueTask> onActivityDelta,
        Func<CancellationToken, ValueTask> onActivityCompleted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(toolExecutor);
        ArgumentNullException.ThrowIfNull(onAnswerDelta);
        ArgumentNullException.ThrowIfNull(onActivityDelta);
        ArgumentNullException.ThrowIfNull(onActivityCompleted);

        await ValidateConfigurationOnceAsync(cancellationToken);
        var agent = CreateAgent(request.Tools, toolExecutor, onActivityDelta, onActivityCompleted);

        return await RunStreamingCoreAsync(
            agent,
            request,
            onAnswerDelta,
            cancellationToken);
    }

    private async Task<FoundryAgentExternalResult> RunStreamingCoreAsync(
        AIAgent agent,
        FoundryAgentExternalRequest request,
        Func<string, CancellationToken, ValueTask> onAnswerDelta,
        CancellationToken cancellationToken)
    {
        var responseText = new List<string>();
        UsageDetails? usage = null;
        var stopwatch = Stopwatch.StartNew();
        var firstEventLogged = false;
        var firstTextLogged = false;
        var messages = CreateMessages(request);

        await foreach (var update in agent.RunStreamingAsync(
                           messages,
                           session: null,
                           cancellationToken: cancellationToken))
        {
            if (!firstEventLogged)
            {
                firstEventLogged = true;
                _logger.LogInformation(
                    "Received the first Foundry event after {ElapsedMilliseconds} ms.",
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            usage = update.Contents
                .OfType<UsageContent>()
                .LastOrDefault()
                ?.Details
                ?? usage;

            var streamableText = GetStreamableText(update);
            if (streamableText is null)
            {
                continue;
            }

            if (!firstTextLogged)
            {
                firstTextLogged = true;
                _logger.LogInformation(
                    "Received the first Foundry answer text after {ElapsedMilliseconds} ms.",
                    stopwatch.Elapsed.TotalMilliseconds);
            }

            responseText.Add(streamableText);
            await onAnswerDelta(streamableText, cancellationToken);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Foundry streaming completed after {ElapsedMilliseconds} ms.",
            stopwatch.Elapsed.TotalMilliseconds);

        return new FoundryAgentExternalResult(
            string.Concat(responseText),
            CreateAgentIdentifier(),
            ToTokenCount(usage?.InputTokenCount),
            ToTokenCount(usage?.OutputTokenCount),
            ModelCallCount: 1);
    }

    private async Task ValidateConfigurationOnceAsync(CancellationToken cancellationToken)
    {
        if (_configurationValidated)
        {
            return;
        }

        await _configurationValidationLock.WaitAsync(cancellationToken);
        try
        {
            if (_configurationValidated)
            {
                return;
            }

            ProjectsAgentVersion agentVersion = await _projectClient.AgentAdministrationClient
                .GetAgentVersionAsync(
                    _settings.AgentName,
                    _settings.AgentVersion,
                    cancellationToken);
            var serializedDefinition = ModelReaderWriter.Write(
                agentVersion.Definition,
                new ModelReaderWriterOptions("W"));
            using var document = JsonDocument.Parse(serializedDefinition.ToStream());
            FoundryAgentDefinitionValidator.Validate(document.RootElement);

            _configurationValidated = true;
            _logger.LogInformation(
                "Validated Foundry agent {AgentName} version {AgentVersion}: all required local function tools are declared and web search is disabled.",
                _settings.AgentName,
                _settings.AgentVersion);
        }
        finally
        {
            _configurationValidationLock.Release();
        }
    }

    private AIAgent CreateAgent(
        IReadOnlyCollection<FoundryAgentExternalToolDefinition> toolDefinitions,
        Func<FoundryAgentExternalToolCall, CancellationToken, Task<string>> toolExecutor,
        Func<string, CancellationToken, ValueTask>? onActivityDelta = null,
        Func<CancellationToken, ValueTask>? onActivityCompleted = null)
    {
        var tools = toolDefinitions
            .Select(definition => CreateTool(
                definition,
                toolExecutor,
                onActivityDelta,
                onActivityCompleted))
            .Cast<AITool>()
            .ToArray();

        _logger.LogInformation(
            "Using Foundry agent {AgentName} version {AgentVersion} from {ProjectEndpoint}.",
            _settings.AgentName,
            _settings.AgentVersion,
            _settings.ProjectEndpoint);

        return _projectClient.AsAIAgent(
            new AgentReference(
                _settings.AgentName,
                _settings.AgentVersion),
            tools: tools);
    }

    private static AIFunction CreateTool(
        FoundryAgentExternalToolDefinition definition,
        Func<FoundryAgentExternalToolCall, CancellationToken, Task<string>> toolExecutor,
        Func<string, CancellationToken, ValueTask>? onActivityDelta,
        Func<CancellationToken, ValueTask>? onActivityCompleted)
    {
        async Task<string> InvokeAsync(
            AIFunctionArguments arguments,
            CancellationToken cancellationToken)
        {
            if (onActivityDelta is not null)
            {
                await onActivityDelta(
                    GetToolStartedMessage(definition),
                    cancellationToken);
            }

            var values = arguments.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.Ordinal);
            var serializedArguments = JsonSerializer.SerializeToElement(values);
            try
            {
                return await toolExecutor(
                    new FoundryAgentExternalToolCall(
                        definition.Name,
                        serializedArguments),
                    cancellationToken);
            }
            finally
            {
                if (onActivityCompleted is not null)
                {
                    await onActivityCompleted(cancellationToken);
                }
            }
        }

        var function = AIFunctionFactory.Create(
            (Func<AIFunctionArguments, CancellationToken, Task<string>>)InvokeAsync,
            new AIFunctionFactoryOptions
            {
                Name = definition.Name,
                Description = definition.Description
            });

        return new JsonSchemaFunction(function, definition.InputSchema);
    }

    private static string GetToolStartedMessage(
        FoundryAgentExternalToolDefinition definition) =>
        $"{HumanizeToolName(definition.DisplayName ?? definition.Name)}…";

    private static string HumanizeToolName(string toolName)
    {
        var words = new List<string>();
        var currentWord = new List<char>();
        foreach (var character in toolName)
        {
            if (character is '_' or '-' or ' ')
            {
                AddCurrentWord();
                continue;
            }

            if (char.IsUpper(character) && currentWord.Count > 0)
            {
                AddCurrentWord();
            }

            currentWord.Add(character);
        }

        AddCurrentWord();
        return string.Join(' ', words);

        void AddCurrentWord()
        {
            if (currentWord.Count == 0)
            {
                return;
            }

            words.Add(new string(currentWord.ToArray()).ToLowerInvariant());
            currentWord.Clear();
        }
    }

    private static IReadOnlyCollection<ChatMessage> CreateMessages(
        FoundryAgentExternalRequest request)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, CurrentTurnInstruction)
        };
        messages.AddRange(request.ConversationHistory
            .Select(message => new ChatMessage(
                message.Role == FoundryAgentExternalMessageRole.Assistant
                    ? ChatRole.Assistant
                    : ChatRole.User,
                message.Content)));
        messages.Add(new ChatMessage(ChatRole.User, request.UserMessage));
        return messages;
    }

    private string CreateAgentIdentifier() =>
        $"{_settings.AgentName}@{_settings.AgentVersion}";

    internal static bool HasStreamableText(string? text) =>
        !string.IsNullOrEmpty(text);

    /// <summary>
    /// AgentResponseUpdate.Text concatene uniquement les TextContent. Les appels
    /// et resultats d'outil peuvent coexister dans la mise a jour sans faire
    /// partie de ce texte et ne doivent donc pas supprimer le fragment diffuse.
    /// </summary>
    internal static string? GetStreamableText(AgentResponseUpdate update) =>
        HasStreamableText(update.Text) ? update.Text : null;

    private static int ToTokenCount(long? tokenCount) =>
        tokenCount is null ? 0 : checked((int)tokenCount.Value);

    private sealed class JsonSchemaFunction(
        AIFunction innerFunction,
        JsonElement inputSchema) : DelegatingAIFunction(innerFunction)
    {
        public override JsonElement JsonSchema => inputSchema;
    }
}
