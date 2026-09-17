using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Microsoft365;

/// <summary>
/// Expose l'avancement d'une reprise a l'interface interne. La reponse ne contient aucun
/// contenu documentaire : uniquement des etats, des compteurs, des dates et un code d'erreur.
/// </summary>
public sealed class Microsoft365ReindexStatusService(
    IMicrosoft365ReindexOperationRepository reindexOperationRepository)
    : IMicrosoft365ReindexStatusService
{
    public async Task<BackofficeMicrosoft365ReindexStatusResponse> GetStatusAsync(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty)
        {
            throw new BadRequestException("Organization identifier is required.");
        }

        if (operationId == Guid.Empty)
        {
            throw new BadRequestException("Reindex operation identifier is required.");
        }

        var operation = await reindexOperationRepository.FindAsync(
            organizationId,
            operationId,
            cancellationToken)
            ?? throw new NotFoundException("Microsoft 365 reindex operation was not found.");

        return new BackofficeMicrosoft365ReindexStatusResponse(
            operation.Id,
            operation.OrganizationId,
            operation.Status.ToString(),
            operation.CompletedSourceCount,
            operation.SourceCount,
            operation.DiscoveredDocumentCount,
            operation.ProcessedDocumentCount,
            operation.IgnoredDocumentCount,
            operation.FailedDocumentCount,
            operation.RequestedAt,
            operation.StartedAt,
            operation.CompletedAt,
            operation.LastErrorCode,
            operation.Reason);
    }
}
