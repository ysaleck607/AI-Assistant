using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IMicrosoft365ReindexOperationRepository
{
    /// <summary>
    /// Enregistre la reprise et une synchronisation complete <c>Pending</c> par bibliotheque,
    /// dans une seule transaction. Retourne <c>null</c> lorsqu'une autre reprise de la meme
    /// organisation est devenue active entre-temps : l'index unique de la base tranche la
    /// course, meme si deux operateurs confirment au meme instant.
    /// </summary>
    Task<Microsoft365ReindexOperation?> CreateAsync(
        Microsoft365ReindexOperation operation,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default);

    Task<Microsoft365ReindexOperation?> FindActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<Microsoft365ReindexOperation?> FindAsync(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Microsoft365ReindexOperation>> GetActiveAsync(
        CancellationToken cancellationToken = default);

    /// <param name="maximumDocumentAttempts">
    /// Nombre de tentatives au-dela duquel un travail documentaire en echec temporaire est
    /// compte comme un echec plutot que comme un travail encore en cours. Sans cette borne,
    /// un document dont l'ACL reste irresolvable est reessaye indefiniment et empecherait la
    /// reprise de se terminer.
    /// </param>
    Task<Microsoft365ReindexProgressData> GetProgressAsync(
        Guid operationId,
        int maximumDocumentAttempts,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Identifiants des documents que la reprise a effectivement revus dans SharePoint.
    /// Un contenu indexe absent de cette liste n'existe plus dans la bibliotheque.
    /// </summary>
    Task<IReadOnlyCollection<string>> GetVisitedDocumentIdsAsync(
        Guid operationId,
        Guid sourceId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Microsoft365ReindexOperation operation,
        CancellationToken cancellationToken = default);
}
