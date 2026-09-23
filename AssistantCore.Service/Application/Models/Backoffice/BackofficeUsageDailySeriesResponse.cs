using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeUsageDailyPointDto(DateOnly Date, int ActiveUsers, int Requests)
{
    public static BackofficeUsageDailyPointDto FromData(BackofficeUsageDailyPointData data) =>
        new(data.Date, data.ActiveUsers, data.Requests);
}

public sealed record BackofficeUsageDailySeriesResponse(IReadOnlyList<BackofficeUsageDailyPointDto> Points)
{
    public static BackofficeUsageDailySeriesResponse FromData(IReadOnlyList<BackofficeUsageDailyPointData> data) =>
        new(data.Select(BackofficeUsageDailyPointDto.FromData).ToList());
}
