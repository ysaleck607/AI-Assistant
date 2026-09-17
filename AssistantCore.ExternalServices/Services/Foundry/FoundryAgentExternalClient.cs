using System.ClientModel.Primitives;
using System.Collections.Concurrent;
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
    private readonly FoundryAgentClientSettings _settings;
    private readonly AIProjectClient _projectClient;
    private readonly ILogger<FoundryAgentExternalClient> _logger;
    private readonly SemaphoreSlim _configurationValidationLock = new(1, 1);
    private readonly ConcurrentDictionary<Guid, ConversationSessionState> _conversationSessions = new();
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

        AgentResponse response;
        if (request.ConversationId == Guid.Empty)
        {
            response = await agent.RunAsync(
                CreateMessages(request),
                cancellationToken: cancellationToken);
        }
        else
        {
            var sessionState = _conversationSessions.GetOrAdd(
                request.ConversationId,
                static _ => new ConversationSessionState());
            await sessionState.Gate.WaitAsync(cancellationToken);
            try
            {
                var isNewSession = sessionState.Session is null;
                sessionState.Session ??= await agent.CreateSessionAsync(cancellationToken);
                response = await agent.RunAsync(
                    isNewSession ? CreateMessages(request) : CreateCurrentMessage(request),
                    sessionState.Session,
                    cancellationToken: cancellationToken);

                _logger.LogInformation(
                    "Foundry conversation {ConversationId} used a {SessionMode} agent session.",
                    request.ConversationId,
                    isNewSession ? "new" : "reused");
            }
            catch
            {
                sessionState.Session = null;
                throw;
            }
            finally
            {
                sessionState.Gate.Release();
            }
        }

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

        if (request.ConversationId == Guid.Empty)
        {
            return await RunStreamingCoreAsync(
                agent,
                request,
                session: null,
                includeHistory: true,
                onAnswerDelta,
                onActivityDelta,
                onActivityCompleted,
                cancellationToken);
        }

        var sessionState = _conversationSessions.GetOrAdd(
            request.ConversationId,
            static _ => new ConversationSessionState());
        await sessionState.Gate.WaitAsync(cancellationToken);
        try
        {
            var isNewSession = sessionState.Session is null;
            sessionState.Session ??= await agent.CreateSessionAsync(cancellationToken);
            var result = await RunStreamingCoreAsync(
                agent,
                request,
                sessionState.Session,
                includeHistory: isNewSession,
                onAnswerDelta,
                onActivityDelta,
                onActivityCompleted,
                cancellationToken);

            _logger.LogInformation(
                "Foundry conversation {ConversationId} used a {SessionMode} streaming agent session.",
                request.ConversationId,
                isNewSession ? "new" : "reused");
            return result;
        }
        catch
        {
            sessionState.Session = null;
            throw;
        }
        finally
        {
            sessionState.Gate.Release();
        }
    }

    private async Task<FoundryAgentExternalResult> RunStreamingCoreAsync(
        AIAgent agent,
        FoundryAgentExternalRequest request,
        AgentSession? session,
        bool includeHistory,
        Func<string, CancellationToken, ValueTask> onAnswerDelta,
        Func<string, CancellationToken, ValueTask> onActivityDelta,
        Func<CancellationToken, ValueTask> onActivityCompleted,
        CancellationToken cancellationToken)
    {
        var responseText = new List<string>();
        UsageDetails? usage = null;
        var stopwatch = Stopwatch.StartNew();
        var firstEventLogged = false;
        var firstTextLogged = false;
        var messages = includeHistory
            ? CreateMessages(request)
            : CreateCurrentMessage(request);

        await foreach (var update in agent.RunStreamingAsync(
                           messages,
                           session,
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
        var messages = request.ConversationHistory
            .Select(message => new ChatMessage(
                message.Role == FoundryAgentExternalMessageRole.Assistant
                    ? ChatRole.Assistant
                    : ChatRole.User,
                message.Content))
            .ToList();

        messages.Add(new ChatMessage(ChatRole.User, request.UserMessage));
        return messages;
    }

    private static IReadOnlyCollection<ChatMessage> CreateCurrentMessage(
        FoundryAgentExternalRequest request)
    {
        return [new ChatMessage(ChatRole.User, request.UserMessage)];
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

    private sealed class ConversationSessionState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public AgentSession? Session { get; set; }
    }

    private sealed class JsonSchemaFunction(
        AIFunction innerFunction,
        JsonElement inputSchema) : DelegatingAIFunction(innerFunction)
    {
        public override JsonElement JsonSchema => inputSchema;
    }
}
