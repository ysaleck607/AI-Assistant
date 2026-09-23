using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed class GetBackofficeUsageByOrganizationQueryHandler(IBackofficeUsageService usageService)
    : IRequestHandler<GetBackofficeUsageByOrganizationQuery, BackofficeUsageByOrganizationResponse>
{
    public async Task<BackofficeUsageByOrganizationResponse> HandleAsync(
        GetBackofficeUsageByOrganizationQuery request,
        CancellationToken cancellationToken)
    {
        return await usageService.GetByOrganizationAsync(request.From, request.To, cancellationToken);
    }
}
