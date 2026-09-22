namespace AssistantCore.Repository.Repositories;

public interface ILlmTokenConsumptionRepository
{
    /// <summary>Atomically adds to the running total for (model, periodStart), creating the row if needed.</summary>
    Task RecordConsumptionAsync(
        string model,
        DateTimeOffset periodStart,
        long tokens,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<long> GetConsumptionAsync(
        string model,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken = default);
}
