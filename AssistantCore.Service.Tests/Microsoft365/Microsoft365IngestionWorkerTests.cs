using System.Diagnostics;
using AssistantCore.Ingestion.Worker;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365IngestionWorkerTests
{
    [Theory, AutoDomainData]
    public async Task Given_AStartupConnection_When_StartAsync_Then_ConnectionIsChecked(
        Guid connectionId)
    {
        // Given
        var orchestrator = new RecordingIngestionOrchestrator();
        var aclReconciliationService = new RecordingAclReconciliationService();
        var maintenanceService = new RecordingSubscriptionMaintenanceService();
        var reconciliationService = new RecordingReconciliationService();
        var services = new ServiceCollection()
            .AddSingleton<IMicrosoft365IngestionOrchestrator>(orchestrator)
            .AddSingleton<IMicrosoft365AclReconciliationService>(aclReconciliationService)
            .AddSingleton<IMicrosoft365SubscriptionMaintenanceService>(maintenanceService)
            .AddSingleton<IMicrosoft365ReconciliationService>(reconciliationService)
            .BuildServiceProvider();
        var worker = new Microsoft365IngestionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Microsoft365WorkerOptions
            {
                RunStartupConnectionCheck = true,
                StartupConnectionId = connectionId
            }),
            NullLogger<Microsoft365IngestionWorker>.Instance);

        // When
        await worker.StartAsync(CancellationToken.None);
        await orchestrator.WaitUntilScheduledAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        // Then
        Assert.Equal(connectionId, orchestrator.ConnectionId);
    }

    [Theory, AutoDomainData]
    public async Task Given_TheWorkerStarts_When_RunMaintenanceAsync_Then_SubscriptionsAreProcessed(
        bool _)
    {
        // Given
        var maintenanceService = new RecordingSubscriptionMaintenanceService();
        var reconciliationService = new RecordingReconciliationService();
        var aclReconciliationService = new RecordingAclReconciliationService();
        var services = new ServiceCollection()
            .AddSingleton<IMicrosoft365AclReconciliationService>(aclReconciliationService)
            .AddSingleton<IMicrosoft365SubscriptionMaintenanceService>(maintenanceService)
            .AddSingleton<IMicrosoft365ReconciliationService>(reconciliationService)
            .BuildServiceProvider();
        var worker = new Microsoft365IngestionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Microsoft365WorkerOptions
            {
                RunStartupConnectionCheck = false,
                MaintenanceIntervalSeconds = 300
            }),
            NullLogger<Microsoft365IngestionWorker>.Instance);

        // When
        await worker.StartAsync(CancellationToken.None);
        await maintenanceService.WaitUntilRunAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, maintenanceService.CallCount);
        Assert.True(aclReconciliationService.CompletedBefore(maintenanceService.FirstRunAt));
    }

    [Theory, AutoDomainData]
    public async Task Given_TheWorkerStarts_When_RunReconciliationAsync_Then_DueSourcesAreProcessed(
        bool _)
    {
        // Given
        var maintenanceService = new RecordingSubscriptionMaintenanceService();
        var reconciliationService = new RecordingReconciliationService();
        var aclReconciliationService = new RecordingAclReconciliationService();
        var services = new ServiceCollection()
            .AddSingleton<IMicrosoft365AclReconciliationService>(aclReconciliationService)
            .AddSingleton<IMicrosoft365SubscriptionMaintenanceService>(maintenanceService)
            .AddSingleton<IMicrosoft365ReconciliationService>(reconciliationService)
            .BuildServiceProvider();
        var worker = new Microsoft365IngestionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Microsoft365WorkerOptions
            {
                RunStartupConnectionCheck = false,
                MaintenanceIntervalSeconds = 300
            }),
            NullLogger<Microsoft365IngestionWorker>.Instance);

        // When
        await worker.StartAsync(CancellationToken.None);
        await reconciliationService.WaitUntilRunAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, reconciliationService.CallCount);
        Assert.True(aclReconciliationService.CompletedBefore(reconciliationService.FirstRunAt));
    }

    [Theory, AutoDomainData]
    public async Task Given_TheWorkerStarts_When_RunAsync_Then_ReindexProgressIsPublished(
        bool _)
    {
        // Given
        var reindexProgressService = new RecordingReindexProgressService();
        var services = new ServiceCollection()
            .AddSingleton<IMicrosoft365ReindexProgressService>(reindexProgressService)
            .AddSingleton<IMicrosoft365AclReconciliationService>(new RecordingAclReconciliationService())
            .AddSingleton<IMicrosoft365SubscriptionMaintenanceService>(
                new RecordingSubscriptionMaintenanceService())
            .AddSingleton<IMicrosoft365ReconciliationService>(new RecordingReconciliationService())
            .BuildServiceProvider();
        var worker = new Microsoft365IngestionWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new Microsoft365WorkerOptions
            {
                RunStartupConnectionCheck = false,
                MaintenanceIntervalSeconds = 300
            }),
            NullLogger<Microsoft365IngestionWorker>.Instance);

        // When
        await worker.StartAsync(CancellationToken.None);
        await reindexProgressService.WaitUntilRunAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await worker.StopAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, reindexProgressService.CallCount);
    }

    private sealed class RecordingIngestionOrchestrator : IMicrosoft365IngestionOrchestrator
    {
        private readonly TaskCompletionSource scheduled = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid? ConnectionId { get; private set; }

        public Task WaitUntilScheduledAsync() => scheduled.Task;

        public Task ScheduleInitialSynchronizationAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default)
        {
            ConnectionId = connectionId;
            scheduled.SetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSubscriptionMaintenanceService
        : IMicrosoft365SubscriptionMaintenanceService
    {
        private readonly TaskCompletionSource ran = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public long? FirstRunAt { get; private set; }

        public Task WaitUntilRunAsync() => ran.Task;

        public Task RunMaintenanceAsync(CancellationToken cancellationToken = default)
        {
            FirstRunAt ??= Stopwatch.GetTimestamp();
            CallCount++;
            ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReconciliationService : IMicrosoft365ReconciliationService
    {
        private readonly TaskCompletionSource ran = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public long? FirstRunAt { get; private set; }

        public Task WaitUntilRunAsync() => ran.Task;

        public Task RunReconciliationAsync(CancellationToken cancellationToken = default)
        {
            FirstRunAt ??= Stopwatch.GetTimestamp();
            CallCount++;
            ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingReindexProgressService : IMicrosoft365ReindexProgressService
    {
        private readonly TaskCompletionSource ran = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount { get; private set; }

        public Task WaitUntilRunAsync() => ran.Task;

        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            CallCount++;
            ran.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingAclReconciliationService : IMicrosoft365AclReconciliationService
    {
        private long? completedAt;

        public Task RunAsync(CancellationToken cancellationToken = default)
        {
            completedAt = Stopwatch.GetTimestamp();
            return Task.CompletedTask;
        }

        public bool CompletedBefore(long? otherOperationAt) =>
            completedAt is not null
            && otherOperationAt is not null
            && completedAt <= otherOperationAt;
    }
}
