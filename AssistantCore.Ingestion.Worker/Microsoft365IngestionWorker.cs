using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Ingestion.Worker;

public sealed class Microsoft365IngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<Microsoft365WorkerOptions> options,
    ILogger<Microsoft365IngestionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var initializationScope = scopeFactory.CreateAsyncScope())
        {
            var indexInitializer = initializationScope.ServiceProvider
                .GetService<IMicrosoft365SearchIndexInitializer>();
            if (indexInitializer is not null)
            {
                await indexInitializer.EnsureCreatedAsync(stoppingToken);
            }
        }

        if (options.Value.RunStartupConnectionCheck
            && options.Value.StartupConnectionId is { } connectionId)
        {
            await using var startupScope = scopeFactory.CreateAsyncScope();
            var orchestrator = startupScope.ServiceProvider
                .GetRequiredService<IMicrosoft365IngestionOrchestrator>();
            await orchestrator.ScheduleInitialSynchronizationAsync(connectionId, stoppingToken);
            logger.LogInformation(
                "Microsoft 365 ingestion startup check accepted connection {ConnectionId}.",
                connectionId);
        }

        logger.LogInformation(
            "Microsoft 365 ingestion worker started subscription maintenance and reconciliation.");
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(options.Value.MaintenanceIntervalSeconds));
        do
        {
            await ProcessPendingIngestionAsync(stoppingToken);

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var retentionService = scope.ServiceProvider
                    .GetRequiredService<IMicrosoft365IngestionRetentionService>();
                await retentionService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Microsoft 365 ingestion retention cycle failed.");
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reindexProgressService = scope.ServiceProvider
                    .GetRequiredService<IMicrosoft365ReindexProgressService>();
                await reindexProgressService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Microsoft 365 reindex progress cycle failed.");
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var aclReconciliationService = scope.ServiceProvider
                    .GetRequiredService<IMicrosoft365AclReconciliationService>();
                await aclReconciliationService.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Microsoft 365 ACL reconciliation cycle failed.");
                continue;
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var maintenanceService = scope.ServiceProvider
                    .GetRequiredService<IMicrosoft365SubscriptionMaintenanceService>();
                await maintenanceService.RunMaintenanceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Microsoft 365 subscription maintenance cycle failed.");
            }

            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciliationService = scope.ServiceProvider
                    .GetRequiredService<IMicrosoft365ReconciliationService>();
                await reconciliationService.RunReconciliationAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Microsoft 365 reconciliation cycle failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessPendingIngestionAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 0; index < options.Value.MaximumSynchronizationsPerCycle; index++)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetService<IMicrosoft365PendingSynchronizationService>();
                if (service is null)
                {
                    break;
                }
                if (!await service.ProcessNextAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft 365 pending ingestion cycle failed.");
        }

        try
        {
            for (var index = 0; index < options.Value.MaximumDocumentsPerCycle; index++)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetService<IMicrosoft365DocumentProcessingService>();
                if (service is null
                    || !await service.ProcessNextAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft 365 document ingestion cycle failed.");
        }

        try
        {
            for (var index = 0; index < options.Value.MaximumListItemsPerCycle; index++)
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider
                    .GetService<IMicrosoft365ListItemProcessingService>();
                if (service is null
                    || !await service.ProcessNextAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft 365 list item ingestion cycle failed.");
        }
    }
}
