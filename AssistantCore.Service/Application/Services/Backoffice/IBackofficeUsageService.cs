using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public interface IBackofficeUsageService
{
    Task<BackofficeUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<BackofficeUsageDailySeriesResponse> GetDailySeriesAsync(
        int days,
        CancellationToken cancellationToken = default);

    Task<BackofficeUsageByOrganizationResponse> GetByOrganizationAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default);
}
