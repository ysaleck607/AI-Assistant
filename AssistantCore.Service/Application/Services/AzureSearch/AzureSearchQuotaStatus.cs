namespace AssistantCore.Service.Application.Services.AzureSearch;

public sealed record AzureSearchQuotaStatus(long StorageUsageInBytes, long StorageQuotaInBytes)
{
    public double UsageRatio =>
        StorageQuotaInBytes <= 0 ? 0d : (double)StorageUsageInBytes / StorageQuotaInBytes;
}
