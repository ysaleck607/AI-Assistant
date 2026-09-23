using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;
using AssistantCore.Service.Application.Services.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeMessageWarnings;

public sealed class GetBackofficeMessageWarningsQueryHandler(
    IBackofficeMessageQualityService messageQualityService)
    : IRequestHandler<GetBackofficeMessageWarningsQuery, BackofficeMessageWarningListResponse>
{
    public async Task<BackofficeMessageWarningListResponse> HandleAsync(
        GetBackofficeMessageWarningsQuery request,
        CancellationToken cancellationToken)
    {
        return await messageQualityService.SearchWarningsAsync(
            request.Page,
            request.PageSize,
            request.OrganizationId,
            request.From,
            request.To,
            request.ContentGapsOnly,
            cancellationToken);
    }
}
