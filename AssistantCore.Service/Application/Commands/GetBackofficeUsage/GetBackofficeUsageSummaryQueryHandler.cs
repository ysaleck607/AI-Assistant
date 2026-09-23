using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed class GetBackofficeUsageSummaryQueryHandler(IBackofficeUsageService usageService)
    : IRequestHandler<GetBackofficeUsageSummaryQuery, BackofficeUsageSummaryDto>
{
    public async Task<BackofficeUsageSummaryDto> HandleAsync(
        GetBackofficeUsageSummaryQuery request,
        CancellationToken cancellationToken)
    {
        return await usageService.GetSummaryAsync(cancellationToken);
    }
}
