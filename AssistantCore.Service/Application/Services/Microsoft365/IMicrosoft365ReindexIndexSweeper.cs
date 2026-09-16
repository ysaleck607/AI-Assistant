namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ReindexIndexSweeper
{
    /// <summary>
    /// Retire de l'index les contenus de la bibliotheque que la reprise n'a pas revus dans
    /// SharePoint. A n'appeler qu'apres une lecture complete reussie de cette bibliotheque.
    /// </summary>
    Task<int> SweepAsync(
        Guid operationId,
        Guid organizationId,
        Guid sourceId,
        CancellationToken cancellationToken = default);
}
