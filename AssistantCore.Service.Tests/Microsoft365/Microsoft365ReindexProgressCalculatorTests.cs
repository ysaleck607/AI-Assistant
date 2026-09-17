using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexProgressCalculatorTests
{
    [Theory, AutoDomainData]
    public void Given_SynchronizationsNotStarted_When_Calculate_Then_StaysPending(Guid sourceId)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [CreateSynchronization(sourceId, Microsoft365SynchronizationStatus.Pending)],
            Microsoft365ReindexDocumentCounts.Empty);

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.Pending, progress.Status);
        Assert.False(progress.IsFinished);
        Assert.Null(progress.StartedAt);
    }

    [Theory, AutoDomainData]
    public void Given_AStartedSynchronization_When_Calculate_Then_ReportsRunningWithTheEarliestStart(
        Guid firstSourceId,
        Guid secondSourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    firstSourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    startedAt: startedAt.AddMinutes(5)),
                CreateSynchronization(
                    secondSourceId,
                    Microsoft365SynchronizationStatus.Running,
                    startedAt: startedAt)
            ],
            new Microsoft365ReindexDocumentCounts(10, 4, 0, 6));

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.Running, progress.Status);
        Assert.Equal(startedAt, progress.StartedAt);
        Assert.Equal(1, progress.CompletedSourceCount);
        Assert.False(progress.IsFinished);
    }

    [Theory, AutoDomainData]
    public void Given_ATemporaryFailure_When_Calculate_Then_ReportsTemporaryFailureAndStaysUnfinished(
        Guid sourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    sourceId,
                    Microsoft365SynchronizationStatus.TemporaryFailure,
                    startedAt: startedAt,
                    lastErrorCode: "MicrosoftGraphInitialDriveSynchronizationFailed")
            ],
            Microsoft365ReindexDocumentCounts.Empty);

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.TemporaryFailure, progress.Status);
        Assert.False(progress.IsFinished);
    }

    [Theory, AutoDomainData]
    public void Given_EveryLibraryAndDocumentDone_When_Calculate_Then_SucceedsAndReportsCounters(
        Guid firstSourceId,
        Guid secondSourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    firstSourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    ignoredCount: 3,
                    startedAt: startedAt),
                CreateSynchronization(
                    secondSourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    ignoredCount: 4,
                    startedAt: startedAt)
            ],
            new Microsoft365ReindexDocumentCounts(20, 18, 2, 0));

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.Succeeded, progress.Status);
        Assert.True(progress.IsFinished);
        Assert.Equal(2, progress.CompletedSourceCount);
        Assert.Equal(20, progress.DiscoveredDocumentCount);
        Assert.Equal(18, progress.ProcessedDocumentCount);
        Assert.Equal(7, progress.IgnoredDocumentCount);
        Assert.Equal(2, progress.FailedDocumentCount);
        Assert.Null(progress.LastErrorCode);
    }

    [Theory, AutoDomainData]
    public void Given_ALibraryInPermanentFailure_When_Calculate_Then_FailsAndKeepsItsErrorCode(
        Guid failedSourceId,
        Guid succeededSourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    failedSourceId,
                    Microsoft365SynchronizationStatus.PermanentFailure,
                    startedAt: startedAt,
                    lastErrorCode: "MicrosoftGraphDriveAccessDenied"),
                CreateSynchronization(
                    succeededSourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    startedAt: startedAt)
            ],
            new Microsoft365ReindexDocumentCounts(5, 5, 0, 0));

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.PermanentFailure, progress.Status);
        Assert.True(progress.IsFinished);
        Assert.Equal("MicrosoftGraphDriveAccessDenied", progress.LastErrorCode);
    }

    [Theory, AutoDomainData]
    public void Given_DocumentsStillQueued_When_Calculate_Then_DoesNotFinishEvenIfLibrariesAreDone(
        Guid sourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    sourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    startedAt: startedAt)
            ],
            new Microsoft365ReindexDocumentCounts(9, 4, 0, 5));

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.False(progress.IsFinished);
        Assert.Equal(Microsoft365ReindexOperationStatus.Running, progress.Status);
    }

    [Theory, AutoDomainData]
    public void Given_ADocumentInPermanentFailure_When_Calculate_Then_StillSucceedsAndCountsTheFailure(
        Guid sourceId,
        DateTimeOffset startedAt)
    {
        // Given
        var data = new Microsoft365ReindexProgressData(
            [
                CreateSynchronization(
                    sourceId,
                    Microsoft365SynchronizationStatus.Succeeded,
                    startedAt: startedAt)
            ],
            new Microsoft365ReindexDocumentCounts(10, 9, 1, 0));

        // When
        var progress = Microsoft365ReindexProgressCalculator.Calculate(data);

        // Then
        Assert.Equal(Microsoft365ReindexOperationStatus.Succeeded, progress.Status);
        Assert.Equal(1, progress.FailedDocumentCount);
    }

    private static Microsoft365ReindexSynchronizationState CreateSynchronization(
        Guid sourceId,
        Microsoft365SynchronizationStatus status,
        int ignoredCount = 0,
        DateTimeOffset? startedAt = null,
        string? lastErrorCode = null) =>
        new(
            Guid.NewGuid(),
            sourceId,
            status,
            ignoredCount,
            startedAt,
            CompletedAt: null,
            lastErrorCode);
}
