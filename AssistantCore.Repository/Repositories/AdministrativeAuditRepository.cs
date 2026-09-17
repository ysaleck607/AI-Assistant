using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Persistence;

namespace AssistantCore.Repository.Repositories;

public sealed class AdministrativeAuditRepository(AssistantCoreDbContext dbContext)
    : IAdministrativeAuditRepository
{
    public void Stage(AdministrativeAuditEntry entry)
    {
        dbContext.AdministrativeAuditEntries.Add(entry);
    }

    public async Task PersistAsync(
        AdministrativeAuditEntry entry,
        CancellationToken cancellationToken = default)
    {
        dbContext.AdministrativeAuditEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
