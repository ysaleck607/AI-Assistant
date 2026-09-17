using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365OutlookSynchronizationRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365OutlookSynchronizationRepository
{
    public Task<Microsoft365Source?> FindForSynchronizationAsync(
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365Sources
            .AsNoTracking()
            .Include(source => source.Microsoft365Connection)
                .ThenInclude(connection => connection.OrganizationConnector)
                    .ThenInclude(connector => connector.Organization)
            .SingleOrDefaultAsync(
                source => source.Id == sourceId
                    && source.Kind == Microsoft365SourceKind.OutlookMailbox,
                cancellationToken);
}
