namespace AssistantCore.ExternalServices.Entities.Azure;

/// <summary>
/// Storage counters from the Azure AI Search service's own /servicestats endpoint.
/// This is a real Azure-side limit (tier storage quota, shared across every index on
/// the service), not something this application tracks or computes itself.
/// </summary>
public sealed record AzureAiSearchServiceStatistics(
    long StorageUsageInBytes,
    long StorageQuotaInBytes);
