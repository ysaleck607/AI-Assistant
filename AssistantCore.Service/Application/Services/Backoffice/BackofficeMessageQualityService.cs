using AssistantCore.Repository.Queries;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Backoffice;

public sealed class BackofficeMessageQualityService(IBackofficeMessageWarningQueries warningQueries)
    : IBackofficeMessageQualityService
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 25;
    private const int MaximumPageSize = 100;

    public async Task<BackofficeMessageWarningListResponse> SearchWarningsAsync(
        int page,
        int pageSize,
        Guid? organizationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = page <= 0 ? DefaultPage : page;
        var normalizedPageSize = pageSize <= 0
            ? DefaultPageSize
            : Math.Min(pageSize, MaximumPageSize);

        var result = await warningQueries.SearchAsync(
            organizationId,
            from,
            to,
            normalizedPage,
            normalizedPageSize,
            cancellationToken);

        return BackofficeMessageWarningListResponse.FromData(result);
    }
}
