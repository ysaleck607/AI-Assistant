namespace AssistantCore.Ingestion.Worker;

public sealed class Microsoft365WorkerOptions
{
    public const string SectionName = "Microsoft365Worker";

    public bool RunStartupConnectionCheck { get; init; }

    public Guid? StartupConnectionId { get; init; }

    public int MaintenanceIntervalSeconds { get; init; } = 300;

    public int MaximumSynchronizationsPerCycle { get; init; } = 10;

    public int MaximumDocumentsPerCycle { get; init; } = 100;

    public int MaximumListItemsPerCycle { get; init; } = 100;
}
