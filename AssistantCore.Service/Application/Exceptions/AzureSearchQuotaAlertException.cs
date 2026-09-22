namespace AssistantCore.Service.Application.Exceptions;

/// <summary>
/// Never thrown to a caller - exists only to carry a human-readable message into an
/// OperationalIncident when the Azure AI Search service's own storage quota (shared
/// across every index on it) crosses the configured alert threshold.
/// </summary>
public sealed class AzureSearchQuotaAlertException(double usageRatio, long usageBytes, long quotaBytes)
    : Exception(
        $"Alerte de quota : l'index Azure AI Search a atteint {usageRatio:P0} de son quota de stockage " +
        $"({FormatMegabytes(usageBytes)} / {FormatMegabytes(quotaBytes)}).")
{
    private static string FormatMegabytes(long bytes) => $"{bytes / 1024.0 / 1024.0:F1} Mo";
}
