using AssistantCore.Repository.Domain.Entities;

namespace AssistantCore.Repository.Repositories;

public interface IOperationalIncidentRepository
{
    /// <summary>
    /// Persiste immediatement un incident. La capture se fait toujours depuis un bloc catch,
    /// hors de toute transaction metier en cours : il n'y a pas de variante Stage.
    /// </summary>
    Task PersistAsync(OperationalIncident incident, CancellationToken cancellationToken = default);
}
