namespace AssistantCore.Service.Application.Services.Conversations.Purge;

public interface IConversationPurgeService
{
    /// <summary>
    /// Traite une demande de purge echue, s'il y en a une. Retourne false quand
    /// il n'y a plus rien a purger, ce qui permet a l'appelant d'attendre avant
    /// de balayer a nouveau.
    /// </summary>
    Task<bool> PurgeNextAsync(CancellationToken cancellationToken = default);
}
