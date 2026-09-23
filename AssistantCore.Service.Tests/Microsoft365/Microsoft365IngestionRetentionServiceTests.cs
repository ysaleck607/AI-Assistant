using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365IngestionRetentionServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_RetentionPolicy_When_RunningCleanup_Then_UsesConfiguredCutoffAndBoundedBatch(
        int deletedRows)
    {
        // Given
        var now = new DateTimeOffset(2026, 9, 19, 2, 0, 0, TimeSpan.Zero);
        var repository = new RecordingRetentionRepository(deletedRows);
        var service = new Microsoft365IngestionRetentionService(
            repository,
            Options.Create(new RetentionOptions
            {
                ConversationRecoveryDays = 30,
                SearchRetentionDays = 30,
                IngestionRetentionDays = 14,
                LogsRetentionDays = 30,
                AuditRetentionDays = 30
            }),
            new FixedTimeProvider(now),
            NullLogger<Microsoft365IngestionRetentionService>.Instance);

        // When
        var result = await service.RunAsync(CancellationToken.None);

        // Then
        Assert.Equal(deletedRows, result);
        Assert.Equal(now.AddDays(-14), repository.Cutoff);
        Assert.Equal(500, repository.BatchSize);
    }

    private sealed class RecordingRetentionRepository(int deletedRows)
        : IMicrosoft365IngestionRetentionRepository
    {
        public DateTimeOffset? Cutoff { get; private set; }
        public int? BatchSize { get; private set; }

        public Task<int> DeleteTerminalWorkBeforeAsync(
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken = default)
        {
            Cutoff = cutoff;
            BatchSize = batchSize;
            return Task.FromResult(deletedRows);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
