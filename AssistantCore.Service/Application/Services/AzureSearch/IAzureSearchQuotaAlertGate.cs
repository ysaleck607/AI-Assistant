namespace AssistantCore.Service.Application.Services.AzureSearch;

/// <summary>
/// Anti-spam for the Azure AI Search storage quota alert: storage usage changes slowly
/// (unlike a per-minute rate limit), so a single alert should not repeat every check
/// cycle for as long as the service stays over threshold.
/// </summary>
public interface IAzureSearchQuotaAlertGate
{
    /// <returns>true the first time since the cooldown expired, then false until it does again.</returns>
    bool TryAcquire();
}
