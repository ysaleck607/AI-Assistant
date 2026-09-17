using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365OutlookMessageIndexingService
{
    Task IndexAsync(
        Organization organization,
        Guid sourceId,
        string mailboxUserId,
        Microsoft365OutlookMessageDelta message,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid organizationId,
        Guid sourceId,
        string messageId,
        CancellationToken cancellationToken = default);
}
