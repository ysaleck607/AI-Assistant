using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365IngestionRetentionService(
    IMicrosoft365IngestionRetentionRepository repository,
    IOptions<RetentionOptions> retentionOptions,
    TimeProvider timeProvider,
    ILogger<Microsoft365IngestionRetentionService> logger) : IMicrosoft365IngestionRetentionService
{
    private const int CleanupBatchSize = 500;

    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-retentionOptions.Value.IngestionRetentionDays);
        var deletedRows = await repository.DeleteTerminalWorkBeforeAsync(
            cutoff,
            CleanupBatchSize,
            cancellationToken);

        if (deletedRows > 0)
        {
            logger.LogInformation(
                "Microsoft 365 ingestion retention removed {DeletedRows} terminal work rows older than {Cutoff}.",
                deletedRows,
                cutoff);
        }

        return deletedRows;
    }
}
