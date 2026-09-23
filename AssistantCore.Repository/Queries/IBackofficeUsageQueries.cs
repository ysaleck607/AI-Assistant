namespace AssistantCore.Repository.Queries;

public interface IBackofficeUsageQueries
{
    Task<BackofficeUsageSummaryData> GetSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackofficeUsageDailyPointData>> GetDailySeriesAsync(
        DateTimeOffset now,
        int days,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackofficeUsageByOrganizationData>> GetByOrganizationAsync(
        DateTimeOffset periodStart,
        DateTimeOffset periodEndExclusive,
        CancellationToken cancellationToken = default);
}
