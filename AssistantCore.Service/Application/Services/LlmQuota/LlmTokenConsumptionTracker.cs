using AssistantCore.Repository.Repositories;

namespace AssistantCore.Service.Application.Services.LlmQuota;

public sealed class LlmTokenConsumptionTracker(
    ILlmTokenConsumptionRepository repository,
    TimeProvider timeProvider) : ILlmTokenConsumptionTracker
{
    public Task RecordConsumptionAsync(
        string model,
        long tokens,
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        return repository.RecordConsumptionAsync(model, GetPeriodStart(now), tokens, now, cancellationToken);
    }

    public Task<long> GetCurrentPeriodConsumptionAsync(
        string model,
        CancellationToken cancellationToken = default) =>
        repository.GetConsumptionAsync(model, GetPeriodStart(timeProvider.GetUtcNow()), cancellationToken);

    private static DateTimeOffset GetPeriodStart(DateTimeOffset now) =>
        new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
}
