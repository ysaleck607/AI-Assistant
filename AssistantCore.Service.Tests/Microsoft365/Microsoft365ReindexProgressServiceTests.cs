using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexProgressServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AReindexStillReading_When_RunAsync_Then_PublishesCountersWithoutFinishing(
        Guid organizationId,
        Guid sourceId,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = CreateOperation(organizationId, sourceCount: 2);
        var repository = new StubReindexOperationRepository
        {
            ActiveOperations = [operation],
            Progress = new Microsoft365ReindexProgressData(
                [
                    CreateSynchronization(sourceId, Microsoft365SynchronizationStatus.Running, startedAt, 2),
                    CreateSynchronization(Guid.NewGuid(), Microsoft365SynchronizationStatus.Pending, null, 0)
                ],
                new Microsoft365ReindexDocumentCounts(12, 5, 1, 6))
        };
        var sweeper = new StubIndexSweeper();
        var service = CreateService(repository, sweeper, now);

        // When
        await service.RunAsync(cancellationToken);

        // Then
        Assert.Same(operation, Assert.Single(repository.SavedOperations));
        Assert.Equal(Microsoft365ReindexOperationStatus.Running, operation.Status);
        Assert.Equal(startedAt, operation.StartedAt);
        Assert.Null(operation.CompletedAt);
        Assert.Equal(12, operation.DiscoveredDocumentCount);
        Assert.Equal(5, operation.ProcessedDocumentCount);
        Assert.Equal(2, operation.IgnoredDocumentCount);
        Assert.Equal(1, operation.FailedDocumentCount);
        Assert.Empty(sweeper.SweptSources);
    }

    [Theory, AutoDomainData]
    public async Task Given_ACompletedReindex_When_RunAsync_Then_RemovesMissingContentAndFinishesTheOperation(
        Guid organizationId,
        Guid sourceId,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = CreateOperation(organizationId, sourceCount: 1);
        var repository = new StubReindexOperationRepository
        {
            ActiveOperations = [operation],
            Progress = new Microsoft365ReindexProgressData(
                [CreateSynchronization(sourceId, Microsoft365SynchronizationStatus.Succeeded, startedAt, 3)],
                new Microsoft365ReindexDocumentCounts(10, 10, 0, 0))
        };
        var sweeper = new StubIndexSweeper();
        var service = CreateService(repository, sweeper, now);

        // When
        await service.RunAsync(cancellationToken);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.Succeeded, operation.Status);
        Assert.Equal(now, operation.CompletedAt);
        Assert.Equal(1, operation.CompletedSourceCount);
        Assert.Equal((operation.Id, organizationId, sourceId), Assert.Single(sweeper.SweptSources));
    }

    [Theory, AutoDomainData]
    public async Task Given_ALibraryThatFailed_When_RunAsync_Then_DoesNotRemoveItsContent(
        Guid organizationId,
        Guid failedSourceId,
        Guid succeededSourceId,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = CreateOperation(organizationId, sourceCount: 2);
        var repository = new StubReindexOperationRepository
        {
            ActiveOperations = [operation],
            Progress = new Microsoft365ReindexProgressData(
                [
                    CreateSynchronization(
                        failedSourceId,
                        Microsoft365SynchronizationStatus.PermanentFailure,
                        startedAt,
                        0,
                        "MicrosoftGraphDriveAccessDenied"),
                    CreateSynchronization(
                        succeededSourceId,
                        Microsoft365SynchronizationStatus.Succeeded,
                        startedAt,
                        0)
                ],
                new Microsoft365ReindexDocumentCounts(4, 4, 0, 0))
        };
        var sweeper = new StubIndexSweeper();
        var service = CreateService(repository, sweeper, now);

        // When
        await service.RunAsync(cancellationToken);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.PermanentFailure, operation.Status);
        Assert.Equal("MicrosoftGraphDriveAccessDenied", operation.LastErrorCode);
        Assert.Equal((operation.Id, organizationId, succeededSourceId), Assert.Single(sweeper.SweptSources));
    }

    [Theory, AutoDomainData]
    public async Task Given_ASweepThatFails_When_RunAsync_Then_KeepsTheOperationActiveForTheNextCycle(
        Guid organizationId,
        Guid sourceId,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var operation = CreateOperation(organizationId, sourceCount: 1);
        var repository = new StubReindexOperationRepository
        {
            ActiveOperations = [operation],
            Progress = new Microsoft365ReindexProgressData(
                [CreateSynchronization(sourceId, Microsoft365SynchronizationStatus.Succeeded, startedAt, 0)],
                new Microsoft365ReindexDocumentCounts(1, 1, 0, 0))
        };
        var service = CreateService(repository, new StubIndexSweeper { Throws = true }, now);

        // When
        await service.RunAsync(cancellationToken);

        // Then
        Assert.Empty(repository.SavedOperations);
        Assert.Equal(Microsoft365ReindexOperationStatus.Pending, operation.Status);
        Assert.Null(operation.CompletedAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_ADocumentRetriedForever_When_RunAsync_Then_BoundsItWithTheConfiguredAttemptLimit(
        Guid organizationId,
        Guid sourceId,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given : un document dont l'ACL reste irresolvable n'est jamais marque en echec
        // permanent. La reprise doit donc borner ses tentatives pour pouvoir se terminer.
        var operation = CreateOperation(organizationId, sourceCount: 1);
        var repository = new StubReindexOperationRepository
        {
            ActiveOperations = [operation],
            Progress = new Microsoft365ReindexProgressData(
                [CreateSynchronization(sourceId, Microsoft365SynchronizationStatus.Succeeded, startedAt, 0)],
                new Microsoft365ReindexDocumentCounts(3, 2, 1, 0))
        };
        var service = CreateService(repository, new StubIndexSweeper(), now);

        // When
        await service.RunAsync(cancellationToken);

        // Then
        Assert.Equal(5, repository.ReceivedMaximumDocumentAttempts);
        Assert.Equal(Microsoft365ReindexOperationStatus.Succeeded, operation.Status);
        Assert.Equal(1, operation.FailedDocumentCount);
    }

    private static Microsoft365ReindexProgressService CreateService(
        StubReindexOperationRepository repository,
        StubIndexSweeper sweeper,
        DateTimeOffset now) =>
        new(
            repository,
            sweeper,
            Options.Create(new Microsoft365Options { DocumentWorkMaximumAttempts = 5 }),
            new FixedTimeProvider(now),
            NullLogger<Microsoft365ReindexProgressService>.Instance);

    private static Microsoft365ReindexOperation CreateOperation(Guid organizationId, int sourceCount) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = Microsoft365ReindexOperationStatus.Pending,
            SourceCount = sourceCount
        };

    private static Microsoft365ReindexSynchronizationState CreateSynchronization(
        Guid sourceId,
        Microsoft365SynchronizationStatus status,
        DateTimeOffset? startedAt,
        int ignoredCount,
        string? lastErrorCode = null) =>
        new(
            Guid.NewGuid(),
            sourceId,
            status,
            ignoredCount,
            startedAt,
            CompletedAt: null,
            lastErrorCode);

    private sealed class StubIndexSweeper : IMicrosoft365ReindexIndexSweeper
    {
        public bool Throws { get; init; }

        public List<(Guid OperationId, Guid OrganizationId, Guid SourceId)> SweptSources { get; } = [];

        public Task<int> SweepAsync(
            Guid operationId,
            Guid organizationId,
            Guid sourceId,
            CancellationToken cancellationToken = default)
        {
            if (Throws)
            {
                throw new InvalidOperationException("Sweep failed.");
            }

            SweptSources.Add((operationId, organizationId, sourceId));
            return Task.FromResult(0);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
