using System.Text.Json;
using AssistantCore.Repository.Domain;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.AiModels;
using AssistantCore.Service.Application.Models.Messages.Connectors;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Models.Messages.Tools;
using AssistantCore.Service.Application.Services.Messages.AgentRuntime;
using AssistantCore.Service.Application.Services.Messages.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Messages;

public sealed class FoundryAgentRuntimeTests
{
    [Theory, AutoDomainData]
    public async Task Given_ConversationHistory_When_RunAsync_Then_DelegatesConversationToFoundry(
        StartedMessageProcessing processing)
    {
        // Given
        processing = processing with
        {
            UserMessage = "Et maintenant ?",
            ConversationHistory =
            [
                new AiConversationMessage(AiConversationRole.User, "Parle-moi du projet."),
                new AiConversationMessage(AiConversationRole.Assistant, "Que veux-tu savoir ?")
            ]
        };
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Voici la suite.", "agent@1", 10, 4, 1));
        var runtime = CreateRuntime(client, new EmptyToolRegistry());

        // When
        var result = await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var request = Assert.Single(client.ReceivedRequests);
        Assert.Equal(processing.ConversationHistory, request.ConversationHistory);
        Assert.Equal("Et maintenant ?", request.UserMessage);
        Assert.Equal("Voici la suite.", result.Content);
        Assert.Equal("agent@1", result.ModelName);
    }

    [Theory, AutoDomainData]
    public async Task Given_AuthorizedEnterpriseSearch_When_RunAsync_Then_DelegatesToolLoopToFoundry(
        StartedMessageProcessing processing)
    {
        // Given
        var evidence = new RetrievedEvidence(
            "evidence-1",
            "Microsoft365",
            "Politique interne",
            "Contenu autorisé.",
            "sharepoint://document",
            "https://contoso.sharepoint.com/document",
            null);
        var router = new RecordingToolExecutionRouter(
            ToolExecutionResult.Succeeded("tool-result", [evidence]));
        var validator = new RecordingToolCallValidator();
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Réponse fondée.", "agent@1", 20, 7, 2),
            invokeFirstTool: true);
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedEnterpriseSearchTool()]),
            validator,
            router);

        // When
        var result = await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var request = Assert.Single(client.ReceivedRequests);
        var tool = Assert.Single(request.Tools);
        Assert.Equal("EnterpriseSearch", tool.Name);
        Assert.Single(validator.ReceivedCalls);
        Assert.Single(router.ReceivedCalls);
        Assert.Equal(evidence, Assert.Single(result.Citations));
        Assert.Equal(1, result.Usage.ToolCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ToolExecutedWithNoEvidence_When_RunAsync_Then_ReturnsNoEvidenceFoundWarning(
        StartedMessageProcessing processing)
    {
        // Given
        var router = new RecordingToolExecutionRouter(
            ToolExecutionResult.Succeeded("tool-result", []));
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Je n'ai pas trouvé d'information à ce sujet.", "agent@1", 20, 7, 2),
            invokeFirstTool: true);
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedEnterpriseSearchTool()]),
            new RecordingToolCallValidator(),
            router);

        // When
        var result = await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var warning = Assert.Single(result.Warnings);
        Assert.StartsWith(MessageWarningMarkers.NoEvidenceFoundPrefix, warning, StringComparison.Ordinal);
        Assert.Empty(result.Citations);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoToolWasCalled_When_RunAsync_Then_DoesNotReportANoEvidenceWarning(
        StartedMessageProcessing processing)
    {
        // Given : une réponse conversationnelle sans recherche n'est pas une lacune de contenu.
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Bonjour, comment puis-je vous aider ?", "agent@1", 10, 4, 1));
        var runtime = CreateRuntime(client, new EmptyToolRegistry());

        // When
        var result = await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        Assert.Empty(result.Warnings);
    }

    [Theory, AutoDomainData]
    public async Task Given_MultipleRetrievedEmails_When_FinalAnswerUsesOneInvoice_Then_ReturnsOnlyThatEmailAsCitation(
        StartedMessageProcessing processing)
    {
        // Given
        var digest = new RetrievedEvidence(
            "mail-digest",
            "Microsoft365",
            "Courriel : Weekly digest: Microsoft service updates",
            "Résumé hebdomadaire des mises à jour Microsoft.",
            "outlook:digest",
            "https://outlook.office365.com/digest",
            null);
        var subscription = new RetrievedEvidence(
            "mail-subscription",
            "Microsoft365",
            "Courriel : Votre abonnement payant démarre le lundi 14 septembre 2026",
            "Votre abonnement Microsoft 365 Business Basic démarre le 14 septembre.",
            "outlook:subscription",
            "https://outlook.office365.com/subscription",
            null);
        var invoice = new RetrievedEvidence(
            "mail-invoice",
            "Microsoft365",
            "Courriel : Votre G185096135 de facture Microsoft est prête",
            "Facture G185096135. Montant à payer : 26,22 $ CAD. Date de facturation : 16 septembre 2026.",
            "outlook:invoice",
            "https://outlook.office365.com/invoice",
            null);
        var router = new RecordingToolExecutionRouter(
            ToolExecutionResult.Succeeded("tool-result", [digest, subscription, invoice]));
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult(
                "Tu dois payer 26,22 $ CAD à Microsoft pour la facture G185096135.",
                "agent@1",
                20,
                7,
                2),
            invokeFirstTool: true);
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedOutlookMailboxTool()]),
            new RecordingToolCallValidator(),
            router);

        // When
        var result = await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var citation = Assert.Single(result.Citations);
        Assert.Equal("mail-invoice", citation.EvidenceId);
        Assert.Equal("Courriel : Votre G185096135 de facture Microsoft est prête", citation.Title);
    }

    [Theory, AutoDomainData]
    public async Task Given_UnauthorizedEnterpriseContext_When_RunAsync_Then_ExposesNoEnterpriseTool(
        StartedMessageProcessing processing)
    {
        // Given
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Réponse.", "agent@1", 8, 2, 1));
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedEnterpriseSearchTool()]));
        var unauthorizedContext = new ConnectorExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid());

        // When
        await runtime.RunAsync(
            new AgentTurnRequest(processing, unauthorizedContext),
            CancellationToken.None);

        // Then
        Assert.Empty(Assert.Single(client.ReceivedRequests).Tools);
    }

    [Theory, AutoDomainData]
    public async Task Given_SearchAndSpreadsheetTools_When_RunAsync_Then_ExposesBothFoundryTools(
        StartedMessageProcessing processing)
    {
        // Given
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Réponse.", "agent@1", 8, 2, 1));
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry(
            [
                CreateAuthorizedEnterpriseSearchTool(),
                CreateAuthorizedSpreadsheetAnalysisTool()
            ]));

        // When
        await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var tools = Assert.Single(client.ReceivedRequests).Tools;
        Assert.Equal(
            ["AnalyzeSpreadsheet", "EnterpriseSearch"],
            tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal));
        var enterpriseSearch = tools.Single(tool => tool.Name == "EnterpriseSearch");
        var spreadsheetAnalysis = tools.Single(tool => tool.Name == "AnalyzeSpreadsheet");
        Assert.Contains("then call AnalyzeSpreadsheet", enterpriseSearch.Description, StringComparison.Ordinal);
        Assert.Contains("call EnterpriseSearch first", spreadsheetAnalysis.Description, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_OutlookMailboxTool_When_RunAsync_Then_ExposesItLikeOtherTools(
        StartedMessageProcessing processing)
    {
        // Given
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Réponse.", "agent@1", 8, 2, 1));
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedOutlookMailboxTool()]));

        // When
        await runtime.RunAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            CancellationToken.None);

        // Then
        var request = Assert.Single(client.ReceivedRequests);
        var tool = Assert.Single(request.Tools);
        Assert.Equal("QueryOutlookMailbox", tool.Name);
        Assert.Contains("includeBody=true", tool.Description, StringComparison.Ordinal);
        Assert.Contains("Never claim that email is unavailable", tool.Description, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_StreamingFoundryResponse_When_RunStreamingAsync_Then_ForwardsAnswerDeltas(
        StartedMessageProcessing processing)
    {
        // Given
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Allô !", "agent@1", 9, 2, 1),
            streamDeltas: ["Allô", " !"]);
        var runtime = CreateRuntime(client, new EmptyToolRegistry());
        var progressMessages = new List<string>();
        var deltas = new List<string>();
        var callbacks = new AgentTurnStreamingCallbacks(
            (message, _) =>
            {
                progressMessages.Add(message);
                return ValueTask.CompletedTask;
            },
            (_, _) => ValueTask.CompletedTask,
            _ => ValueTask.CompletedTask,
            (delta, _) =>
            {
                deltas.Add(delta);
                return ValueTask.CompletedTask;
            });

        // When
        var result = await runtime.RunStreamingAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            callbacks,
            CancellationToken.None);

        // Then
        Assert.Equal(["Préparation de la réponse…"], progressMessages);
        Assert.Equal(["Allô", " !"], deltas);
        Assert.Equal("Allô !", result.Content);
    }

    [Theory, AutoDomainData]
    public async Task Given_OutlookToolExecution_When_RunStreamingAsync_Then_ShowsSourceAgnosticProgressInsteadOfToolName(
        StartedMessageProcessing processing)
    {
        // Given
        var client = new RecordingFoundryAgentClient(
            new FoundryAgentClientResult("Réponse.", "agent@1", 9, 2, 1),
            invokeFirstTool: true,
            rawActivityDelta: "QueryOutlookMailbox…");
        var runtime = CreateRuntime(
            client,
            new StubToolRegistry([CreateAuthorizedOutlookMailboxTool()]),
            new RecordingToolCallValidator(),
            new RecordingToolExecutionRouter(ToolExecutionResult.Succeeded("tool-result", [])));
        var progressMessages = new List<string>();
        var activityMessages = new List<string>();
        var callbacks = new AgentTurnStreamingCallbacks(
            (message, _) =>
            {
                progressMessages.Add(message);
                return ValueTask.CompletedTask;
            },
            (message, _) =>
            {
                activityMessages.Add(message);
                return ValueTask.CompletedTask;
            },
            _ => ValueTask.CompletedTask,
            (_, _) => ValueTask.CompletedTask);

        // When
        await runtime.RunStreamingAsync(
            new AgentTurnRequest(processing, CreateValidExecutionContext()),
            callbacks,
            CancellationToken.None);

        // Then
        Assert.Equal(2, progressMessages.Count);
        Assert.Equal("Préparation de la réponse…", progressMessages[0]);
        Assert.DoesNotContain("courriel", progressMessages[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outlook", progressMessages[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("classeur", progressMessages[1], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(progressMessages, message => message.Contains("QueryOutlookMailbox", StringComparison.Ordinal));
        Assert.Empty(activityMessages);
    }

    private static FoundryAgentRuntime CreateRuntime(
        IFoundryAgentClient client,
        IAiToolRegistry toolRegistry,
        IAiToolCallValidator? validator = null,
        IToolExecutionRouter? router = null) =>
        new(
            client,
            toolRegistry,
            validator ?? new ThrowingToolCallValidator(),
            router ?? new ThrowingToolExecutionRouter(),
            Options.Create(CreateOptions()),
            NullLogger<FoundryAgentRuntime>.Instance);

    private static AgentRuntimeOptions CreateOptions() =>
        new()
        {
            MaximumExecutionTimeSeconds = 30,
            RetrievalCandidateLimit = 10,
            FinalEvidenceLimit = 5
        };

    private static ConnectorExecutionContext CreateValidExecutionContext() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "tenant-id",
            Guid.NewGuid(),
            IdentityProvider.MicrosoftEntraId,
            UserEmail: "member@synaptix.local");

    private static AiToolDefinition CreateAuthorizedEnterpriseSearchTool() =>
        new(
            AiToolNames.SearchMicrosoft365,
            "Search authorized enterprise information.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string" },
                    sourceTypes = new { type = new[] { "array", "null" } },
                    dateFrom = new { type = new[] { "string", "null" } },
                    dateTo = new { type = new[] { "string", "null" } }
                },
                required = new[] { "query", "sourceTypes", "dateFrom", "dateTo" },
                additionalProperties = false
            }));

    private static AiToolDefinition CreateAuthorizedSpreadsheetAnalysisTool() =>
        new(
            AiToolNames.AnalyzeMicrosoft365Spreadsheet,
            "Analyze an authorized spreadsheet.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    fileName = new { type = "string" }
                },
                required = new[] { "fileName" },
                additionalProperties = false
            }));

    private static AiToolDefinition CreateAuthorizedOutlookMailboxTool() =>
        new(
            AiToolNames.QueryOutlookMailbox,
            "Query Outlook mailbox.",
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    query = new { type = new[] { "string", "null" } },
                    sender = new { type = new[] { "string", "null" } },
                    recipient = new { type = new[] { "string", "null" } },
                    scope = new { type = "string" },
                    dateFrom = new { type = new[] { "string", "null" } },
                    dateTo = new { type = new[] { "string", "null" } },
                    includeBody = new { type = "boolean" },
                    limit = new { type = "integer" }
                },
                required = new[] { "query", "sender", "recipient", "scope", "dateFrom", "dateTo", "includeBody", "limit" },
                additionalProperties = false
            }));

    private sealed class RecordingFoundryAgentClient(
        FoundryAgentClientResult result,
        bool invokeFirstTool = false,
        IReadOnlyCollection<string>? streamDeltas = null,
        string? rawActivityDelta = null) : IFoundryAgentClient
    {
        public List<FoundryAgentClientRequest> ReceivedRequests { get; } = [];

        public async Task<FoundryAgentClientResult> RunAsync(
            FoundryAgentClientRequest request,
            FoundryAgentToolExecutor toolExecutor,
            CancellationToken cancellationToken)
        {
            ReceivedRequests.Add(request);
            if (invokeFirstTool && request.Tools.Count > 0)
            {
                await InvokeFirstToolAsync(request, toolExecutor, cancellationToken);
            }

            return result;
        }

        public async Task<FoundryAgentClientResult> RunStreamingAsync(
            FoundryAgentClientRequest request,
            FoundryAgentToolExecutor toolExecutor,
            Func<string, CancellationToken, ValueTask> onAnswerDelta,
            Func<string, CancellationToken, ValueTask> onActivityDelta,
            Func<CancellationToken, ValueTask> onActivityCompleted,
            CancellationToken cancellationToken)
        {
            ReceivedRequests.Add(request);
            if (rawActivityDelta is not null)
            {
                await onActivityDelta(rawActivityDelta, cancellationToken);
                await onActivityCompleted(cancellationToken);
            }
            if (invokeFirstTool && request.Tools.Count > 0)
            {
                await InvokeFirstToolAsync(request, toolExecutor, cancellationToken);
            }
            foreach (var delta in streamDeltas ?? [])
            {
                await onAnswerDelta(delta, cancellationToken);
            }

            return result;
        }

        private static Task<string> InvokeFirstToolAsync(
            FoundryAgentClientRequest request,
            FoundryAgentToolExecutor toolExecutor,
            CancellationToken cancellationToken) =>
            toolExecutor(
                new FoundryAgentToolCall(
                    request.Tools.First().Name,
                    JsonSerializer.SerializeToElement(new
                    {
                        query = "information pertinente",
                        sourceTypes = (string[]?)null,
                        dateFrom = (string?)null,
                        dateTo = (string?)null
                    })),
                cancellationToken);
    }

    private sealed class EmptyToolRegistry : IAiToolRegistry
    {
        public Task<IReadOnlyCollection<AiToolDefinition>> GetAvailableToolsAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<AiToolDefinition>>([]);

        public void InvalidateCache(Guid organizationId)
        {
        }
    }

    private sealed class StubToolRegistry(
        IReadOnlyCollection<AiToolDefinition> tools) : IAiToolRegistry
    {
        public Task<IReadOnlyCollection<AiToolDefinition>> GetAvailableToolsAsync(
            Guid organizationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(tools);

        public void InvalidateCache(Guid organizationId)
        {
        }
    }

    private sealed class RecordingToolCallValidator : IAiToolCallValidator
    {
        public List<AiRequestedToolCall> ReceivedCalls { get; } = [];

        public Task<ValidatedToolCall> ValidateAsync(
            AiRequestedToolCall requestedToolCall,
            IReadOnlyCollection<AiToolDefinition> availableTools,
            CancellationToken cancellationToken)
        {
            ReceivedCalls.Add(requestedToolCall);
            return Task.FromResult(new ValidatedToolCall(
                requestedToolCall.CallId,
                requestedToolCall.ToolName,
                requestedToolCall.Arguments));
        }
    }

    private sealed class RecordingToolExecutionRouter(
        ToolExecutionResult result) : IToolExecutionRouter
    {
        public List<ValidatedToolCall> ReceivedCalls { get; } = [];

        public Task<ToolExecutionResult> ExecuteAsync(
            ValidatedToolCall toolCall,
            ConnectorExecutionContext executionContext,
            CancellationToken cancellationToken)
        {
            ReceivedCalls.Add(toolCall);
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingToolCallValidator : IAiToolCallValidator
    {
        public Task<ValidatedToolCall> ValidateAsync(
            AiRequestedToolCall requestedToolCall,
            IReadOnlyCollection<AiToolDefinition> availableTools,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingToolExecutionRouter : IToolExecutionRouter
    {
        public Task<ToolExecutionResult> ExecuteAsync(
            ValidatedToolCall toolCall,
            ConnectorExecutionContext executionContext,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
