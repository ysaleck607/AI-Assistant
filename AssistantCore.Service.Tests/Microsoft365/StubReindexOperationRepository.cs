using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;

namespace AssistantCore.Service.Tests.Microsoft365;

/// <summary>
/// Double partage par les tests de la reindexation administrative : il conserve ce qui a ete
/// cree et enregistre afin que chaque test verifie un comportement plutot que la persistence.
/// </summary>
internal sealed class StubReindexOperationRepository : IMicrosoft365ReindexOperationRepository
{
    public Microsoft365ReindexOperation? ActiveOperation { get; init; }

    public Microsoft365ReindexOperation? KnownOperation { get; init; }

    public Microsoft365ReindexProgressData Progress { get; init; } =
        new([], Microsoft365ReindexDocumentCounts.Empty);

    public IReadOnlyCollection<Microsoft365ReindexOperation> ActiveOperations { get; init; } = [];

    public IReadOnlyCollection<string> VisitedDocumentIds { get; init; } = [];

    public List<(Microsoft365ReindexOperation Operation, IReadOnlyCollection<Guid> SourceIds)>
        CreatedOperations { get; } = [];

    public List<Microsoft365ReindexOperation> SavedOperations { get; } = [];

    public List<(Guid OperationId, Guid SourceId)> VisitedDocumentRequests { get; } = [];

    public CancellationToken ReceivedCancellationToken { get; private set; }

    /// <summary>Simule la course perdue contre une autre reprise de la meme organisation.</summary>
    public bool RejectsCreation { get; init; }

    public Task<Microsoft365ReindexOperation?> CreateAsync(
        Microsoft365ReindexOperation operation,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        CreatedOperations.Add((operation, sourceIds));
        ReceivedCancellationToken = cancellationToken;
        return Task.FromResult(RejectsCreation ? null : operation);
    }

    public Task<Microsoft365ReindexOperation?> FindActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveOperation);

    public Task<Microsoft365ReindexOperation?> FindAsync(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(KnownOperation is not null
            && KnownOperation.Id == operationId
            && KnownOperation.OrganizationId == organizationId
                ? KnownOperation
                : null);

    public Task<IReadOnlyCollection<Microsoft365ReindexOperation>> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ActiveOperations);

    public int? ReceivedMaximumDocumentAttempts { get; private set; }

    public Task<Microsoft365ReindexProgressData> GetProgressAsync(
        Guid operationId,
        int maximumDocumentAttempts,
        CancellationToken cancellationToken = default)
    {
        ReceivedMaximumDocumentAttempts = maximumDocumentAttempts;
        return Task.FromResult(Progress);
    }

    public Task<IReadOnlyCollection<string>> GetVisitedDocumentIdsAsync(
        Guid operationId,
        Guid sourceId,
        CancellationToken cancellationToken = default)
    {
        VisitedDocumentRequests.Add((operationId, sourceId));
        return Task.FromResult(VisitedDocumentIds);
    }

    public Task SaveAsync(
        Microsoft365ReindexOperation operation,
        CancellationToken cancellationToken = default)
    {
        SavedOperations.Add(operation);
        return Task.CompletedTask;
    }
}
