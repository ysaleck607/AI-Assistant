using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public interface IBackofficeMessageQualityService
{
    Task<BackofficeMessageWarningListResponse> SearchWarningsAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool contentGapsOnly,
        CancellationToken cancellationToken = default);
}
