namespace AssistantCore.Service.Application.Configuration;

public sealed class LlmQuotaAlertOptions
{
    public const string SectionName = "LlmQuotaAlert";

    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 60;

    /// <summary>0.70 = alert once a model's consumption reaches 70% of its monthly limit.</summary>
    public double ThresholdRatio { get; init; } = 0.70;

    /// <summary>
    /// Monthly token budget for the chat/planning model (gpt-5.5). 0 = not configured yet,
    /// that model's check is skipped rather than raising a false alarm.
    /// </summary>
    public long ChatModelMonthlyTokenLimit { get; init; }

    /// <summary>Monthly token budget for the embedding model (text-embedding-3-small). Same 0 rule.</summary>
    public long EmbeddingModelMonthlyTokenLimit { get; init; }
}
