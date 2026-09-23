using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;

namespace AssistantCore.Repository.Repositories;

public sealed class OperationalIncidentRepository(AssistantCoreDbContext dbContext)
    : IOperationalIncidentRepository
{
    public async Task PersistAsync(
        OperationalIncident incident,
        CancellationToken cancellationToken = default)
    {
        dbContext.OperationalIncidents.Add(incident);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
