using AssistantCore.ExternalServices.Services.Azure;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Services.AzureSearch;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Infrastructure.AzureSearch;

public sealed class AzureSearchQuotaCheckerAdapter(
    AzureAiSearchIndexClient client,
    IOptions<AzureAiSearchOptions> searchOptions) : IAzureSearchQuotaChecker
{
    public async Task<AzureSearchQuotaStatus> GetQuotaStatusAsync(CancellationToken cancellationToken = default)
    {
        var search = searchOptions.Value;

        try
        {
            var statistics = await client.GetServiceStatisticsAsync(
                search.Endpoint,
                search.ApiKey,
                cancellationToken);

            return new AzureSearchQuotaStatus(statistics.StorageUsageInBytes, statistics.StorageQuotaInBytes);
        }
        catch (AzureAiSearchExternalException exception)
        {
            throw new AzureAiSearchUnavailableException(
                "Azure AI Search service statistics request failed.",
                exception);
        }
    }
}
