using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Conversations.Purge;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Conversations;

public sealed class ConversationPurgeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Given_NothingDue_When_PurgeNextAsync_Then_ReportsThereIsNothingToDo()
    {
        // Given
        var repository = new RecordingPurgeRepository { NextRequest = null };
        var service = CreateService(repository);

        // When
        var purged = await service.PurgeNextAsync();

        // Then
        Assert.False(purged);
        Assert.Empty(repository.Operations);
    }

    [Fact]
    public async Task Given_AFreshRequest_When_PurgeNextAsync_Then_RunsEveryStepInOrder()
    {
        // Given
        var repository = new RecordingPurgeRepository
        {
            NextRequest = CreateRequest(ConversationPurgeStep.DeleteMessageSources)
        };
        var service = CreateService(repository);

        // When
        await service.PurgeNextAsync();

        // Then
        Assert.Equal(
            [
                "DeleteMessageSources",
                "Advance:DeleteMessages",
                "DeleteMessages",
                "Advance:DeleteConversation",
                "DeleteConversation",
                "Advance:Verify",
                "Verify",
                "Complete"
            ],
            repository.Operations);
    }

    [Theory]
    [InlineData(ConversationPurgeStep.DeleteMessages, "DeleteMessageSources")]
    [InlineData(ConversationPurgeStep.DeleteConversation, "DeleteMessages")]
    [InlineData(ConversationPurgeStep.Verify, "DeleteConversation")]
    public async Task Given_AnInterruptedRequest_When_PurgeNextAsync_Then_ConfirmedStepsAreNotReplayed(
        ConversationPurgeStep resumeStep,
        string skippedOperation)
    {
        // Given
        // Une reprise repart de l'etape enregistree : rejouer une etape confirmee
        // ferait un travail inutile et fausserait les compteurs de la preuve.
        var repository = new RecordingPurgeRepository
        {
            NextRequest = CreateRequest(resumeStep)
        };
        var service = CreateService(repository);

        // When
        await service.PurgeNextAsync();

        // Then
        Assert.DoesNotContain(skippedOperation, repository.Operations);
        Assert.Contains("Complete", repository.Operations);
    }

    [Fact]
    public async Task Given_AVerificationThatFails_When_PurgeNextAsync_Then_TheRequestIsNotCompleted()
    {
        // Given
        // Marquer Completed sans verifier laisserait croire qu'une donnee a
        // disparu alors qu'elle subsiste.
        var repository = new RecordingPurgeRepository
        {
            NextRequest = CreateRequest(ConversationPurgeStep.DeleteMessageSources),
            VerificationResult = false
        };
        var service = CreateService(repository);

        // When
        await service.PurgeNextAsync();

        // Then
        Assert.DoesNotContain("Complete", repository.Operations);
        Assert.Contains("Fail:temporary", repository.Operations);
    }

    [Fact]
    public async Task Given_AFailingStep_When_PurgeNextAsync_Then_TheFailureIsTemporaryAndRescheduled()
    {
        // Given
        var repository = new RecordingPurgeRepository
        {
            NextRequest = CreateRequest(ConversationPurgeStep.DeleteMessageSources),
            ThrowOnDeleteMessages = true
        };
        var service = CreateService(repository);

        // When
        await service.PurgeNextAsync();

        // Then
        Assert.Contains("Fail:temporary", repository.Operations);
        Assert.NotNull(repository.LastNextAttemptAt);
        Assert.True(repository.LastNextAttemptAt > Now);
    }

    [Fact]
    public async Task Given_TheLastAllowedAttempt_When_ItFails_Then_TheFailureBecomesPermanent()
    {
        // Given
        // Au-dela du nombre maximal de tentatives, la demande cesse d'etre rejouee
        // et reste visible pour alerte plutot que de boucler indefiniment.
        var request = CreateRequest(ConversationPurgeStep.DeleteMessageSources);
        request.AttemptCount = 5;
        var repository = new RecordingPurgeRepository
        {
            NextRequest = request,
            ThrowOnDeleteMessages = true
        };
        var service = CreateService(repository);

        // When
        await service.PurgeNextAsync();

        // Then
        Assert.Contains("Fail:permanent", repository.Operations);
        Assert.Null(repository.LastNextAttemptAt);
    }

    private static ConversationPurgeService CreateService(IConversationPurgeRepository repository) =>
        new(
            repository,
            Options.Create(new ConversationPurgeOptions
            {
                LeaseMinutes = 10,
                MaximumAttempts = 5,
                RetryBackoffMinutes = 5
            }),
            new FixedTimeProvider(Now),
            NullLogger<ConversationPurgeService>.Instance);

    private static ConversationPurgeRequest CreateRequest(ConversationPurgeStep step) =>
        new()
        {
            Id = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            OrganizationId = Guid.NewGuid(),
            RequestedAt = Now.AddDays(-31),
            PurgeAfter = Now.AddMinutes(-1),
            Status = ConversationPurgeStatus.Processing,
            Step = step,
            AttemptCount = 1
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingPurgeRepository : IConversationPurgeRepository
    {
        public List<string> Operations { get; } = [];

        public ConversationPurgeRequest? NextRequest { get; init; }

        public bool VerificationResult { get; init; } = true;

        public bool ThrowOnDeleteMessages { get; init; }

        public DateTimeOffset? LastNextAttemptAt { get; private set; }

        public Task<ConversationPurgeRequest?> ClaimNextAsync(
            Guid leaseId,
            DateTimeOffset now,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(NextRequest);

        public Task<int> DeleteMessageSourcesAsync(
            Guid purgeRequestId,
            Guid leaseId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("DeleteMessageSources");
            return Task.FromResult(3);
        }

        public Task<int> DeleteMessagesAsync(
            Guid purgeRequestId,
            Guid leaseId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("DeleteMessages");
            if (ThrowOnDeleteMessages) throw new InvalidOperationException("Database unavailable.");
            return Task.FromResult(2);
        }

        public Task DeleteConversationAsync(
            Guid purgeRequestId,
            Guid leaseId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("DeleteConversation");
            return Task.CompletedTask;
        }

        public Task<bool> VerifyPurgedAsync(
            Guid purgeRequestId,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Verify");
            return Task.FromResult(VerificationResult);
        }

        public Task AdvanceAsync(
            Guid purgeRequestId,
            Guid leaseId,
            ConversationPurgeStep nextStep,
            CancellationToken cancellationToken = default)
        {
            Operations.Add($"Advance:{nextStep}");
            return Task.CompletedTask;
        }

        public Task CompleteAsync(
            Guid purgeRequestId,
            Guid leaseId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default)
        {
            Operations.Add("Complete");
            return Task.CompletedTask;
        }

        public Task FailAsync(
            Guid purgeRequestId,
            Guid leaseId,
            string errorCode,
            bool isPermanent,
            DateTimeOffset? nextAttemptAt,
            CancellationToken cancellationToken = default)
        {
            Operations.Add(isPermanent ? "Fail:permanent" : "Fail:temporary");
            LastNextAttemptAt = nextAttemptAt;
            return Task.CompletedTask;
        }
    }
}
