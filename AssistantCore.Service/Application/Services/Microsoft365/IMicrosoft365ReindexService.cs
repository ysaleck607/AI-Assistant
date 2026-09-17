using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ReindexService
{
    /// <summary>
    /// Enregistre une reprise complete des bibliotheques SharePoint activees de l'organisation
    /// et retourne immediatement son identifiant, sans attendre le traitement des documents.
    /// </summary>
    Task<BackofficeMicrosoft365ReindexResponse> RequestReindexAsync(
        Guid organizationId,
        string? reason,
        CancellationToken cancellationToken = default);
}
