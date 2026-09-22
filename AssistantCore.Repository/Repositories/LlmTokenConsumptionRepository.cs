using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class LlmTokenConsumptionRepository(AssistantCoreDbContext dbContext)
    : ILlmTokenConsumptionRepository
{
    // MERGE fait de l'incrementation un aller-retour atomique en base - indispensable ici,
    // plusieurs conversations/lots d'embedding peuvent completer en parallele et
    // incrementer le meme (Model, PeriodStart) en meme temps.
    public async Task RecordConsumptionAsync(
        string model,
        DateTimeOffset periodStart,
        long tokens,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (tokens <= 0)
        {
            return;
        }

        var newId = Guid.NewGuid();
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            MERGE INTO [dbo].[LlmTokenConsumption] AS target
            USING (SELECT {model} AS Model, {periodStart} AS PeriodStart) AS source
            ON target.[Model] = source.Model AND target.[PeriodStart] = source.PeriodStart
            WHEN MATCHED THEN
                UPDATE SET [TokensConsumed] = target.[TokensConsumed] + {tokens}, [UpdatedAt] = {now}
            WHEN NOT MATCHED THEN
                INSERT ([Id], [Model], [PeriodStart], [TokensConsumed], [UpdatedAt])
                VALUES ({newId}, {model}, {periodStart}, {tokens}, {now});
            """, cancellationToken);
    }

    public async Task<long> GetConsumptionAsync(
        string model,
        DateTimeOffset periodStart,
        CancellationToken cancellationToken = default)
    {
        var consumption = await dbContext.LlmTokenConsumptions
            .AsNoTracking()
            .Where(row => row.Model == model && row.PeriodStart == periodStart)
            .Select(row => (long?)row.TokensConsumed)
            .SingleOrDefaultAsync(cancellationToken);

        return consumption ?? 0;
    }
}
