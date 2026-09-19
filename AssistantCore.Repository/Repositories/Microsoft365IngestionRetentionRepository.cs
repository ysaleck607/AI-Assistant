using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365IngestionRetentionRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365IngestionRetentionRepository
{
    public async Task<int> DeleteTerminalWorkBeforeAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        }

        var documentRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE TOP ({batchSize})
            FROM [dbo].[Microsoft365DocumentWork]
            WHERE [CompletedAt] IS NOT NULL
              AND [CompletedAt] <= {cutoff}
              AND [Status] IN (N'Completed', N'PermanentFailure');
            """, cancellationToken);

        var listItemRows = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE TOP ({batchSize})
            FROM [dbo].[Microsoft365ListItemWork]
            WHERE [CompletedAt] IS NOT NULL
              AND [CompletedAt] <= {cutoff}
              AND [Status] IN (N'Completed', N'PermanentFailure');
            """, cancellationToken);

        return documentRows + listItemRows;
    }
}
