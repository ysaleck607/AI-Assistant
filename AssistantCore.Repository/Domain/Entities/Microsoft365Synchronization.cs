using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Domain.Entities;

public sealed class Microsoft365Synchronization
{
    public Guid Id { get; set; }

    public Guid Microsoft365SourceId { get; set; }

    public Guid? Microsoft365ReindexOperationId { get; set; }

    public Microsoft365SynchronizationType Type { get; set; }

    public Microsoft365SynchronizationStatus Status { get; set; }

    public int AttemptCount { get; set; }

    public int CreatedCount { get; set; }

    public int ModifiedCount { get; set; }

    public int DeletedCount { get; set; }

    public int IgnoredCount { get; set; }

    public int FailedCount { get; set; }

    public DateTimeOffset RequestedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastErrorCode { get; set; }

    public Microsoft365Source Microsoft365Source { get; set; } = null!;

    public Microsoft365ReindexOperation? Microsoft365ReindexOperation { get; set; }
}
