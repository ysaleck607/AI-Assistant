namespace AssistantCore.Service.Application.Services.LlmQuota;

public interface ILlmTokenConsumptionTracker
{
    /// <summary>Adds to the running total for this model's current calendar-month period.</summary>
    Task RecordConsumptionAsync(string model, long tokens, CancellationToken cancellationToken = default);

    Task<long> GetCurrentPeriodConsumptionAsync(string model, CancellationToken cancellationToken = default);
}
