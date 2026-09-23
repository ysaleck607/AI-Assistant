namespace AssistantCore.Service.Application.Configuration;

public sealed class AzureSearchQuotaAlertOptions
{
    public const string SectionName = "AzureSearchQuotaAlert";

    public bool Enabled { get; init; } = true;

    public int IntervalMinutes { get; init; } = 60;

    /// <summary>0.70 = alert once storage usage reaches 70% of the service's quota.</summary>
    public double ThresholdRatio { get; init; } = 0.70;
}
