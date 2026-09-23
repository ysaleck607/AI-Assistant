using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed class GetBackofficeUsageDailySeriesQueryHandler(IBackofficeUsageService usageService)
    : IRequestHandler<GetBackofficeUsageDailySeriesQuery, BackofficeUsageDailySeriesResponse>
{
    public async Task<BackofficeUsageDailySeriesResponse> HandleAsync(
        GetBackofficeUsageDailySeriesQuery request,
        CancellationToken cancellationToken)
    {
        return await usageService.GetDailySeriesAsync(request.Days, cancellationToken);
    }
}
