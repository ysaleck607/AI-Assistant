namespace AssistantCore.Repository.Queries;

public sealed record BackofficeUsageSummaryData(
    int DailyActiveUsers,
    int RequestsToday,
    double RequestsPerActiveUser,
    int WeeklyActiveUsers,
    int MonthlyActiveUsers,
    int NewUsersToday,
    int ReturningUsersToday);
