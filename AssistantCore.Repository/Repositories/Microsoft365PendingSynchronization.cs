using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories;

public sealed record Microsoft365PendingSynchronization(
    Guid SynchronizationId,
    Guid SourceId,
    Guid OrganizationId,
    Microsoft365SourceKind SourceKind,
    Microsoft365SourceStatus SourceStatus,
    bool IsIndexed,
    Microsoft365SynchronizationType Type,
    Guid? ReindexOperationId = null);
