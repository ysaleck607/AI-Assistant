namespace AssistantCore.Service.Application.Services.Microsoft365;

public interface IMicrosoft365ReindexProgressService
{
    /// <summary>
    /// Met a jour l'etat et les compteurs des reprises en cours, applique les suppressions
    /// lorsqu'une reprise a fini son parcours, puis la finalise.
    /// </summary>
    Task RunAsync(CancellationToken cancellationToken = default);
}
