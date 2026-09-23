namespace AssistantCore.Repository.Queries;

public sealed record BackofficeUsageDailyPointData(
    DateOnly Date,
    int ActiveUsers,
    int Requests);
