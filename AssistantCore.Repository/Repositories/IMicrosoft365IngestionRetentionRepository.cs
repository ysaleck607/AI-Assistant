namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365IngestionRetentionRepository
{
    Task<int> DeleteTerminalWorkBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default);
}
