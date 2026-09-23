using AssistantCore.Repository.Domain;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Models.Messages.AgentRuntime;
using AssistantCore.Service.Application.Models.Messages.Lifecycle;
using AssistantCore.Service.Application.Services.Incidents;
using AssistantCore.Service.Application.Services.LlmQuota;
using AssistantCore.Service.Application.Services.Messages.Lifecycle;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Messages;

public sealed class MessageProcessingLifecycleServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_NoConversationIdentifier_When_StartAsync_Then_CreatesConversationAndStartsMessage(
        Organization organization,
        OrganizationMember member,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.StartAsync(
            null,
            "Question already validated",
            organization,
            member,
            cancellationTokenSource.Token);

        Assert.NotNull(repository.CreatedConversation);
        Assert.NotNull(repository.CreatedFirstMessage);
        Assert.Equal(organization.Id, repository.CreatedConversation.OrganizationId);
        Assert.Equal(member.Id, repository.CreatedConversation.OwnerMemberId);
        Assert.Equal(ConversationStatus.Active, repository.CreatedConversation.Status);
        Assert.Equal("Question already validated", repository.CreatedConversation.Title);
        Assert.Equal(now, repository.CreatedConversation.CreatedAt);
        Assert.Equal(now, repository.CreatedConversation.UpdatedAt);
        Assert.Equal(repository.CreatedConversation.Id, repository.CreatedFirstMessage.ConversationId);
        Assert.Equal(MessageRole.User, repository.CreatedFirstMessage.Role);
        Assert.Equal(MessageProcessingStatus.Pending, repository.CreatedFirstMessage.ProcessingStatus);
        Assert.Equal("Question already validated", repository.CreatedFirstMessage.Content);
        Assert.Equal(MessageProcessingStatus.InProgress, repository.ReceivedProcessingStatus);
        Assert.Equal(repository.CreatedFirstMessage.Id, repository.ReceivedMessageId);
        Assert.Equal(organization.Id, result.OrganizationId);
        Assert.Equal(member.Id, result.OwnerMemberId);
        Assert.Equal(repository.CreatedConversation.Id, result.ConversationId);
        Assert.Equal(repository.CreatedFirstMessage.Id, result.UserMessageId);
        Assert.Equal(["CreateConversation", "UpdateStatus"], repository.Operations);
        Assert.Equal(cancellationTokenSource.Token, repository.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoConversationIdentifier_When_StartAsync_Then_DescribesTheCreatedConversation(
        Organization organization,
        OrganizationMember member,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);

        var result = await service.StartAsync(
            null,
            "Politique de teletravail",
            organization,
            member,
            CancellationToken.None);

        Assert.NotNull(repository.CreatedConversation);
        Assert.NotNull(result.CreatedConversation);
        Assert.Equal(repository.CreatedConversation.Id, result.CreatedConversation.Id);
        Assert.Equal("Politique de teletravail", result.CreatedConversation.Title);
        Assert.Equal(nameof(ConversationStatus.Active), result.CreatedConversation.Status);
        Assert.Equal(1, result.CreatedConversation.Version);
        Assert.Equal(now, result.CreatedConversation.CreatedAt);
        Assert.Equal(now, result.CreatedConversation.UpdatedAt);
        Assert.Null(result.CreatedConversation.LastMessagePreview);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOwnedConversation_When_StartAsync_Then_DoesNotDescribeTheConversation(
        Organization organization,
        OrganizationMember member,
        Conversation conversation,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        conversation.OrganizationId = organization.Id;
        conversation.OwnerMemberId = member.Id;
        var repository = new RecordingConversationRepository { FoundConversation = conversation };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);

        var result = await service.StartAsync(
            conversation.Id,
            "Another validated question",
            organization,
            member,
            CancellationToken.None);

        Assert.Null(result.CreatedConversation);
    }

    [Theory, AutoDomainData]
    public async Task Given_ALongFirstMessage_When_StartAsync_Then_TruncatesTheDerivedTitle(
        Organization organization,
        OrganizationMember member,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);
        var longMessage = new string('a', 250);

        await service.StartAsync(null, longMessage, organization, member, CancellationToken.None);

        Assert.NotNull(repository.CreatedConversation);
        Assert.Equal(200, repository.CreatedConversation.Title.Length);
        Assert.EndsWith("…", repository.CreatedConversation.Title, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOwnedConversation_When_StartAsync_Then_AddsAndStartsMessage(
        Organization organization,
        OrganizationMember member,
        Conversation conversation,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        conversation.OrganizationId = organization.Id;
        conversation.OwnerMemberId = member.Id;
        var repository = new RecordingConversationRepository { FoundConversation = conversation };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.StartAsync(
            conversation.Id,
            "Another validated question",
            organization,
            member,
            cancellationTokenSource.Token);

        Assert.Null(repository.CreatedConversation);
        Assert.NotNull(repository.AddedUserMessage);
        Assert.Equal(conversation.Id, repository.AddedUserMessage.ConversationId);
        Assert.Equal(MessageRole.User, repository.AddedUserMessage.Role);
        Assert.Equal(MessageProcessingStatus.Pending, repository.AddedUserMessage.ProcessingStatus);
        Assert.Equal("Another validated question", repository.AddedUserMessage.Content);
        Assert.Equal(MessageProcessingStatus.InProgress, repository.ReceivedProcessingStatus);
        Assert.Equal(repository.AddedUserMessage.Id, result.UserMessageId);
        Assert.Equal(
            ["FindConversation", "GetConversationHistory", "AddUserMessage", "UpdateStatus"],
            repository.Operations);
        Assert.Equal(cancellationTokenSource.Token, repository.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnavailableConversation_When_StartAsync_Then_ThrowsNotFoundWithoutAddingMessage(
        Organization organization,
        OrganizationMember member)
    {
        member.OrganizationId = organization.Id;
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.StartAsync(Guid.NewGuid(), "Question", organization, member, CancellationToken.None));

        Assert.Equal("Conversation not found.", exception.Message);
        Assert.Null(repository.AddedUserMessage);
        Assert.Null(repository.ReceivedProcessingStatus);
        Assert.Equal(["FindConversation"], repository.Operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_AConversationRemovedBeforeMessageIsAdded_When_StartAsync_Then_ThrowsNotFoundWithoutStartingMessage(
        Organization organization,
        OrganizationMember member,
        Conversation conversation)
    {
        member.OrganizationId = organization.Id;
        conversation.OrganizationId = organization.Id;
        conversation.OwnerMemberId = member.Id;
        var repository = new RecordingConversationRepository
        {
            FoundConversation = conversation,
            ReturnNullWhenAddingMessage = true
        };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.StartAsync(conversation.Id, "Question", organization, member, CancellationToken.None));

        Assert.Equal("Conversation not found.", exception.Message);
        Assert.Null(repository.ReceivedProcessingStatus);
        Assert.Equal(
            ["FindConversation", "GetConversationHistory", "AddUserMessage"],
            repository.Operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_AMemberFromAnotherOrganization_When_StartAsync_Then_ThrowsBeforePersistence(
        Organization organization,
        OrganizationMember member)
    {
        member.OrganizationId = Guid.NewGuid();
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartAsync(null, "Question", organization, member, CancellationToken.None));

        Assert.Contains("does not belong", exception.Message, StringComparison.Ordinal);
        Assert.Empty(repository.Operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_AValidAgentTurn_When_CompleteAsync_Then_PersistsAssistantResponseSourcesAndWarnings(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        var evidence = new RetrievedEvidence(
            "evidence-1",
            "ERP",
            "Montreal inventory",
            "248 units available",
            "inventory-item-1",
            null,
            completedAt.AddMinutes(-1));
        var agentTurnResult = new AgentTurnResult(
            "There are 248 units available.",
            "gpt",
            [evidence],
            ["Quebec inventory was unavailable."],
            new AgentTurnUsage(TimeSpan.FromSeconds(2), 100, 20, 2, 1));
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(completedAt), NullLogger<MessageProcessingLifecycleService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        var result = await service.CompleteAsync(processing, agentTurnResult, cancellationTokenSource.Token);

        Assert.NotNull(repository.CompletedAssistantMessage);
        Assert.Equal(MessageRole.Assistant, repository.CompletedAssistantMessage.Role);
        Assert.Equal(MessageProcessingStatus.Completed, repository.CompletedAssistantMessage.ProcessingStatus);
        Assert.Equal(agentTurnResult.Content, repository.CompletedAssistantMessage.Content);
        Assert.Equal(agentTurnResult.ModelName, repository.CompletedAssistantMessage.Model);
        Assert.Equal(completedAt, repository.CompletedAssistantMessage.CreatedAt);
        var persistedSource = Assert.Single(repository.CompletedSources);
        Assert.Equal(evidence.SourceType, persistedSource.SourceType);
        Assert.Equal(evidence.Title, persistedSource.Title);
        Assert.Equal(evidence.Reference, persistedSource.Reference);
        Assert.Equal(evidence.OccurredAt, persistedSource.SourceDate);
        var persistedWarning = Assert.Single(repository.CompletedWarnings);
        Assert.Equal("Quebec inventory was unavailable.", persistedWarning.Content);
        Assert.Equal(repository.CompletedAssistantMessage.Id, result.AssistantMessageId);
        Assert.Equal(completedAt, result.CreatedAt);
        Assert.Equal(cancellationTokenSource.Token, repository.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_ACompletedMessage_When_CompleteAsync_Then_RecordsTokenConsumptionForThePlanningModel(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        // Given
        var agentTurnResult = new AgentTurnResult(
            "Reponse.",
            "gpt",
            [],
            [],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 100, 20, 1, 0));
        var repository = new RecordingConversationRepository();
        var tracker = new RecordingLlmTokenConsumptionTracker();
        var service = new MessageProcessingLifecycleService(
            repository,
            tracker,
            new NoOpOperationalIncidentReporter(), CreateSearchOptions(),
            new StubTimeProvider(completedAt),
            NullLogger<MessageProcessingLifecycleService>.Instance);

        // When
        await service.CompleteAsync(processing, agentTurnResult, CancellationToken.None);

        // Then
        var recorded = Assert.Single(tracker.RecordedConsumptions);
        Assert.Equal("gpt-5.5", recorded.Model);
        Assert.Equal(120, recorded.Tokens);
    }

    [Theory, AutoDomainData]
    public async Task Given_TokenTrackingFails_When_CompleteAsync_Then_StillReturnsTheCompletedMessage(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        // Given : le suivi de quota ne doit jamais faire echouer une reponse deja rendue.
        var agentTurnResult = new AgentTurnResult(
            "Reponse.",
            "gpt",
            [],
            [],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 100, 20, 1, 0));
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(
            repository,
            new ThrowingLlmTokenConsumptionTracker(),
            new NoOpOperationalIncidentReporter(), CreateSearchOptions(),
            new StubTimeProvider(completedAt),
            NullLogger<MessageProcessingLifecycleService>.Instance);

        // When
        var result = await service.CompleteAsync(processing, agentTurnResult, CancellationToken.None);

        // Then
        Assert.Equal(repository.CompletedAssistantMessage!.Id, result.AssistantMessageId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ANoEvidenceWarning_When_CompleteAsync_Then_ReportsAContentGapAlertWithTheQuestionAndResponse(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        // Given
        var agentTurnResult = new AgentTurnResult(
            "Je n'ai pas trouve d'information a ce sujet.",
            "gpt",
            [],
            [$"{MessageWarningMarkers.NoEvidenceFoundPrefix} Aucune preuve documentaire trouvée pour répondre à cette question."],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 100, 20, 1, 0));
        var repository = new RecordingConversationRepository();
        var reporter = new RecordingOperationalIncidentReporter();
        var service = new MessageProcessingLifecycleService(
            repository,
            new NoOpLlmTokenConsumptionTracker(),
            reporter,
            CreateSearchOptions(),
            new StubTimeProvider(completedAt),
            NullLogger<MessageProcessingLifecycleService>.Instance);

        // When
        await service.CompleteAsync(processing, agentTurnResult, CancellationToken.None);

        // Then
        var reported = Assert.Single(reporter.ReportedIncidents);
        var alert = Assert.IsType<ContentGapAlertException>(reported.Exception);
        Assert.Contains(processing.UserMessage, alert.Message, StringComparison.Ordinal);
        Assert.Contains(agentTurnResult.Content, alert.Message, StringComparison.Ordinal);
        Assert.Equal(processing.OrganizationId, reported.OrganizationId);
        Assert.Equal(processing.OwnerMemberId, reported.OrganizationMemberId);
        Assert.Equal("Conversation", reported.RelatedResourceType);
        Assert.Equal(processing.ConversationId, reported.RelatedResourceId);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoNoEvidenceWarning_When_CompleteAsync_Then_DoesNotReportAContentGapAlert(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        // Given
        var agentTurnResult = new AgentTurnResult(
            "Voici la reponse.",
            "gpt",
            [],
            [],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 100, 20, 1, 0));
        var repository = new RecordingConversationRepository();
        var reporter = new RecordingOperationalIncidentReporter();
        var service = new MessageProcessingLifecycleService(
            repository,
            new NoOpLlmTokenConsumptionTracker(),
            reporter,
            CreateSearchOptions(),
            new StubTimeProvider(completedAt),
            NullLogger<MessageProcessingLifecycleService>.Instance);

        // When
        await service.CompleteAsync(processing, agentTurnResult, CancellationToken.None);

        // Then
        Assert.Empty(reporter.ReportedIncidents);
    }

    [Theory, AutoDomainData]
    public async Task Given_ContentGapReportingFails_When_CompleteAsync_Then_StillReturnsTheCompletedMessage(
        StartedMessageProcessing processing,
        DateTimeOffset completedAt)
    {
        // Given : une panne d'alerte ne doit jamais faire echouer une reponse deja rendue.
        var agentTurnResult = new AgentTurnResult(
            "Je n'ai pas trouve d'information a ce sujet.",
            "gpt",
            [],
            [$"{MessageWarningMarkers.NoEvidenceFoundPrefix} Aucune preuve documentaire trouvée pour répondre à cette question."],
            new AgentTurnUsage(TimeSpan.FromSeconds(1), 100, 20, 1, 0));
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(
            repository,
            new NoOpLlmTokenConsumptionTracker(),
            new ThrowingOperationalIncidentReporter(),
            CreateSearchOptions(),
            new StubTimeProvider(completedAt),
            NullLogger<MessageProcessingLifecycleService>.Instance);

        // When
        var result = await service.CompleteAsync(processing, agentTurnResult, CancellationToken.None);

        // Then
        Assert.Equal(repository.CompletedAssistantMessage!.Id, result.AssistantMessageId);
    }

    [Theory, AutoDomainData]
    public async Task Given_RepositoryRejectsCompletion_When_CompleteAsync_Then_ThrowsNotFound(
        StartedMessageProcessing processing,
        AgentTurnResult agentTurnResult,
        DateTimeOffset completedAt)
    {
        var repository = new RecordingConversationRepository { ReturnNullWhenCompletingMessage = true };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(completedAt), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.CompleteAsync(processing, agentTurnResult, CancellationToken.None));

        Assert.Equal("Conversation not found.", exception.Message);
    }

    [Theory]
    [InlineAutoDomainData(false, MessageProcessingStatus.Failed)]
    [InlineAutoDomainData(true, MessageProcessingStatus.Cancelled)]
    public async Task Given_AProcessingFailure_When_FailAsync_Then_PersistsTheExpectedTerminalStatus(
        bool wasCancelled,
        MessageProcessingStatus expectedStatus,
        StartedMessageProcessing processing,
        DateTimeOffset failedAt)
    {
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(failedAt), NullLogger<MessageProcessingLifecycleService>.Instance);
        using var cancellationTokenSource = new CancellationTokenSource();

        await service.FailAsync(
            processing,
            new MessageProcessingFailure("  provider_unavailable  ", wasCancelled),
            cancellationTokenSource.Token);

        Assert.Equal(expectedStatus, repository.ReceivedFailureStatus);
        Assert.Equal("provider_unavailable", repository.ReceivedErrorCode);
        Assert.Equal(failedAt, repository.ReceivedFailureDate);
        Assert.Equal(cancellationTokenSource.Token, repository.ReceivedCancellationToken);
        Assert.Equal(["FailMessage"], repository.Operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_RepositoryRejectsFailure_When_FailAsync_Then_ThrowsNotFound(
        StartedMessageProcessing processing)
    {
        var repository = new RecordingConversationRepository { ReturnFalseWhenFailingMessage = true };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() =>
            service.FailAsync(
                processing,
                new MessageProcessingFailure("provider_unavailable", false),
                CancellationToken.None));

        Assert.Equal("Conversation not found.", exception.Message);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnInvalidErrorCode_When_FailAsync_Then_ThrowsBeforePersistence(
        StartedMessageProcessing processing)
    {
        var repository = new RecordingConversationRepository();
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.FailAsync(
                processing,
                new MessageProcessingFailure(new string('x', 101), false),
                CancellationToken.None));

        Assert.Equal("errorCode", exception.ParamName);
        Assert.Empty(repository.Operations);
    }

    [Theory, AutoDomainData]
    public async Task Given_RepositoryFailure_When_FailAsync_Then_PropagatesTheException(
        StartedMessageProcessing processing)
    {
        var persistenceException = new InvalidOperationException("Persistence failed.");
        var repository = new RecordingConversationRepository { FailureException = persistenceException };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(DateTimeOffset.UtcNow), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.FailAsync(
                processing,
                new MessageProcessingFailure("provider_unavailable", false),
                CancellationToken.None));

        Assert.Same(persistenceException, exception);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnArchivedConversation_When_StartAsync_Then_ThrowsAConflictWithTheArchivedCode(
        Organization organization,
        OrganizationMember member,
        Conversation conversation,
        DateTimeOffset now)
    {
        member.OrganizationId = organization.Id;
        conversation.OrganizationId = organization.Id;
        conversation.OwnerMemberId = member.Id;
        conversation.Status = ConversationStatus.Archived;
        var repository = new RecordingConversationRepository { FoundConversation = conversation };
        var service = new MessageProcessingLifecycleService(repository, new NoOpLlmTokenConsumptionTracker(), new NoOpOperationalIncidentReporter(), CreateSearchOptions(), new StubTimeProvider(now), NullLogger<MessageProcessingLifecycleService>.Instance);

        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            service.StartAsync(
                conversation.Id,
                "Nouvelle question",
                organization,
                member,
                CancellationToken.None));

        Assert.Equal(ConflictException.ConversationArchived, exception.ErrorCode);
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private static IOptions<AzureAiSearchOptions> CreateSearchOptions() =>
        Options.Create(new AzureAiSearchOptions { PlanningModelName = "gpt-5.5" });

    private sealed class NoOpLlmTokenConsumptionTracker : ILlmTokenConsumptionTracker
    {
        public Task RecordConsumptionAsync(
            string model,
            long tokens,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<long> GetCurrentPeriodConsumptionAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }

    private sealed class RecordingLlmTokenConsumptionTracker : ILlmTokenConsumptionTracker
    {
        public List<(string Model, long Tokens)> RecordedConsumptions { get; } = [];

        public Task RecordConsumptionAsync(
            string model,
            long tokens,
            CancellationToken cancellationToken = default)
        {
            RecordedConsumptions.Add((model, tokens));
            return Task.CompletedTask;
        }

        public Task<long> GetCurrentPeriodConsumptionAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }

    private sealed class ThrowingLlmTokenConsumptionTracker : ILlmTokenConsumptionTracker
    {
        public Task RecordConsumptionAsync(
            string model,
            long tokens,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated tracking failure.");

        public Task<long> GetCurrentPeriodConsumptionAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);
    }

    private sealed class NoOpOperationalIncidentReporter : IOperationalIncidentReporter
    {
        public Task ReportAsync(
            OperationalIncidentReport report,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingOperationalIncidentReporter : IOperationalIncidentReporter
    {
        public List<OperationalIncidentReport> ReportedIncidents { get; } = [];

        public Task ReportAsync(
            OperationalIncidentReport report,
            CancellationToken cancellationToken = default)
        {
            ReportedIncidents.Add(report);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingOperationalIncidentReporter : IOperationalIncidentReporter
    {
        public Task ReportAsync(
            OperationalIncidentReport report,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Simulated reporting failure.");
    }

    private sealed class RecordingConversationRepository : IConversationRepository
    {
        public Task<ConversationUpdateResult> UpdateConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            int? expectedVersion,
            string? title,
            ConversationStatus? status,
            DateTimeOffset updatedAt,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ConversationDeleteStatus> SoftDeleteConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            DateTimeOffset deletedAt,
            DateTimeOffset purgeAfter,
            string correlationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Conversation? FoundConversation { get; init; }
        public bool ReturnNullWhenAddingMessage { get; init; }
        public bool ReturnNullWhenCompletingMessage { get; init; }
        public bool ReturnFalseWhenFailingMessage { get; init; }
        public Exception? FailureException { get; init; }
        public Conversation? CreatedConversation { get; private set; }
        public Message? CreatedFirstMessage { get; private set; }
        public Message? AddedUserMessage { get; private set; }
        public Guid? ReceivedMessageId { get; private set; }
        public MessageProcessingStatus? ReceivedProcessingStatus { get; private set; }
        public Message? CompletedAssistantMessage { get; private set; }
        public IReadOnlyCollection<MessageSource> CompletedSources { get; private set; } = [];
        public IReadOnlyCollection<MessageWarning> CompletedWarnings { get; private set; } = [];
        public MessageProcessingStatus? ReceivedFailureStatus { get; private set; }
        public string? ReceivedErrorCode { get; private set; }
        public DateTimeOffset? ReceivedFailureDate { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }
        public List<string> Operations { get; } = [];

        public Task<(Conversation Conversation, Message UserMessage)> CreateConversationWithFirstMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Conversation conversation,
            Message userMessage,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("CreateConversation");
            conversation.Version = 1;
            CreatedConversation = conversation;
            CreatedFirstMessage = userMessage;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult((conversation, userMessage));
        }

        public Task<Conversation?> FindConversationAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("FindConversation");
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(FoundConversation);
        }

        public Task<ConversationMessagePage> ListMessagesAsync(
            Guid conversationId,
            int limit,
            DateTimeOffset? cursorCreatedAt,
            Guid? cursorId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ConversationMessageItem>> GetConversationHistoryAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("GetConversationHistory");
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult<IReadOnlyList<ConversationMessageItem>>([]);
        }

        public Task<ConversationListPage> ListConversationsAsync(
            Guid organizationId,
            Guid ownerMemberId,
            ConversationStatus status,
            int limit,
            DateTimeOffset? cursorUpdatedAt,
            Guid? cursorId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("ListConversations");
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(new ConversationListPage([], false));
        }

        public Task<Message?> AddUserMessageAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Message userMessage,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("AddUserMessage");
            AddedUserMessage = userMessage;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(ReturnNullWhenAddingMessage ? null : userMessage);
        }

        public Task<bool> UpdateMessageProcessingStatusAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid messageId,
            MessageProcessingStatus status,
            DateTimeOffset updatedAt,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("UpdateStatus");
            ReceivedMessageId = messageId;
            ReceivedProcessingStatus = status;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(true);
        }

        public Task<Message?> CompleteMessageWithAssistantResponseAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid userMessageId,
            Message assistantMessage,
            IReadOnlyCollection<MessageSource> sources,
            IReadOnlyCollection<MessageWarning> warnings,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("CompleteMessage");
            CompletedAssistantMessage = assistantMessage;
            CompletedSources = sources;
            CompletedWarnings = warnings;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(ReturnNullWhenCompletingMessage ? null : assistantMessage);
        }

        public Task<bool> FailMessageProcessingAsync(
            Guid organizationId,
            Guid ownerMemberId,
            Guid conversationId,
            Guid userMessageId,
            MessageProcessingStatus failureStatus,
            string errorCode,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("FailMessage");
            ReceivedFailureStatus = failureStatus;
            ReceivedErrorCode = errorCode;
            ReceivedFailureDate = failedAt;
            ReceivedCancellationToken = cancellationToken;

            if (FailureException is not null)
            {
                return Task.FromException<bool>(FailureException);
            }

            return Task.FromResult(!ReturnFalseWhenFailingMessage);
        }

        public Task<int> ReencryptAllConversationTitlesAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReencryptAllMessageContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReencryptAllMessageWarningContentAsync(
            int batchSize,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
