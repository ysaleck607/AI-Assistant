using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Models.Microsoft365;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365DocumentWorkFactory
{
    /// <param name="reindexOperationId">
    /// Reprise administrative a l'origine de la lecture, lorsqu'il y en a une. Elle entre dans
    /// la cle de deduplication afin qu'un ancien travail en echec ne bloque pas le nouveau.
    /// </param>
    Microsoft365DocumentWorkData Create(
        Microsoft365Drive drive,
        Microsoft365DriveItemDelta item,
        DateTimeOffset createdAt,
        Guid? reindexOperationId = null);
}
