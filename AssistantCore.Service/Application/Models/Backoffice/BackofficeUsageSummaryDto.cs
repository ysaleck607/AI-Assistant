using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeUsageSummaryDto(
    int DailyActiveUsers,
    int RequestsToday,
    double RequestsPerActiveUser,
    int WeeklyActiveUsers,
    int MonthlyActiveUsers,
    int NewUsersToday,
    int ReturningUsersToday)
{
    public static BackofficeUsageSummaryDto FromData(BackofficeUsageSummaryData data) => new(
        data.DailyActiveUsers,
        data.RequestsToday,
        data.RequestsPerActiveUser,
        data.WeeklyActiveUsers,
        data.MonthlyActiveUsers,
        data.NewUsersToday,
        data.ReturningUsersToday);
}
