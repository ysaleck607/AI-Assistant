using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Publie l'avancement des reprises en cours pour l'interface interne. Une reprise se termine
/// lorsque toutes ses bibliotheques et tous leurs documents ont quitte les etats transitoires.
/// </summary>
public sealed class Microsoft365ReindexProgressService(
    IMicrosoft365ReindexOperationRepository reindexOperationRepository,
    IMicrosoft365ReindexIndexSweeper indexSweeper,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    ILogger<Microsoft365ReindexProgressService> logger) : IMicrosoft365ReindexProgressService
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var operations = await reindexOperationRepository.GetActiveAsync(cancellationToken);
        foreach (var operation in operations)
        {
            try
            {
                await AdvanceAsync(operation, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Une reprise bloquee ne doit pas empecher les autres d'avancer. Elle est
                // reexaminee au prochain cycle du worker.
                logger.LogError(
                    exception,
                    "Microsoft 365 reindex progress failed. OperationId={OperationId}",
                    operation.Id);
            }
        }
    }

    private async Task AdvanceAsync(
        Microsoft365ReindexOperation operation,
        CancellationToken cancellationToken)
    {
        var progressData = await reindexOperationRepository.GetProgressAsync(
            operation.Id,
            options.Value.DocumentWorkMaximumAttempts,
            cancellationToken);
        var progress = Microsoft365ReindexProgressCalculator.Calculate(progressData);

        if (progress.IsFinished)
        {
            await RemoveContentMissingFromSharePointAsync(operation, progressData, cancellationToken);
            operation.CompletedAt = timeProvider.GetUtcNow();
        }

        operation.Status = progress.Status;
        operation.CompletedSourceCount = progress.CompletedSourceCount;
        operation.DiscoveredDocumentCount = progress.DiscoveredDocumentCount;
        operation.ProcessedDocumentCount = progress.ProcessedDocumentCount;
        operation.IgnoredDocumentCount = progress.IgnoredDocumentCount;
        operation.FailedDocumentCount = progress.FailedDocumentCount;
        operation.StartedAt = progress.StartedAt;
        operation.LastErrorCode = progress.LastErrorCode;
        await reindexOperationRepository.SaveAsync(operation, cancellationToken);

        if (progress.IsFinished)
        {
            logger.LogInformation(
                "Microsoft 365 reindex finished. OperationId={OperationId} OrganizationId={OrganizationId} "
                + "Status={Status} Libraries={CompletedLibraryCount}/{LibraryCount} "
                + "Documents={ProcessedDocumentCount}/{DiscoveredDocumentCount} "
                + "FailedDocumentCount={FailedDocumentCount}",
                operation.Id,
                operation.OrganizationId,
                operation.Status,
                operation.CompletedSourceCount,
                operation.SourceCount,
                operation.ProcessedDocumentCount,
                operation.DiscoveredDocumentCount,
                operation.FailedDocumentCount);
        }
    }

    /// <summary>
    /// Seule une bibliotheque entierement relue permet de conclure qu'un contenu a disparu.
    /// Une lecture en echec laisse donc son index inchange.
    /// </summary>
    private async Task RemoveContentMissingFromSharePointAsync(
        Microsoft365ReindexOperation operation,
        Microsoft365ReindexProgressData progressData,
        CancellationToken cancellationToken)
    {
        foreach (var synchronization in progressData.Synchronizations)
        {
            if (synchronization.Status != Microsoft365SynchronizationStatus.Succeeded)
            {
                continue;
            }

            await indexSweeper.SweepAsync(
                operation.Id,
                operation.OrganizationId,
                synchronization.SourceId,
                cancellationToken);
        }
    }
}
