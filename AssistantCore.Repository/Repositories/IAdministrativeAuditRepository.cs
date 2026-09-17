using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IAdministrativeAuditRepository
{
    /// <summary>
    /// Suit l'entree pour insertion sans appeler SaveChanges : l'appelant est responsable de
    /// la persister dans la meme transaction que la modification metier qu'elle journalise.
    /// </summary>
    void Stage(AdministrativeAuditEntry entry);

    /// <summary>
    /// Persiste immediatement une entree d'audit lorsqu'aucune autre modification metier
    /// n'existe dans la commande courante, comme une reevaluation d'acces en lecture seule.
    /// </summary>
    Task PersistAsync(AdministrativeAuditEntry entry, CancellationToken cancellationToken = default);
}
