namespace AssistantCore.Service.Application.Services.AzureSearch;

public interface IAzureSearchQuotaChecker
{
    Task<AzureSearchQuotaStatus> GetQuotaStatusAsync(CancellationToken cancellationToken = default);
}
