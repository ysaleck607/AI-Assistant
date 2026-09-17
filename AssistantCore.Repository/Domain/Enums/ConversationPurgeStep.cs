namespace AssistantCore.Repository.Domain.Enums;

/// <summary>
/// Etapes de la purge, dans l'ordre ou elles doivent etre executees. L'etape
/// courante est persistee apres chaque succes : un arret brutal reprend a la
/// derniere etape confirmee au lieu de tout recommencer.
///
/// Aucune etape ne vise Azure AI Search : l'index ne contient que du contenu
/// Microsoft 365, jamais de conversation ni de message.
/// </summary>
public enum ConversationPurgeStep
{
    DeleteMessageSources = 1,
    DeleteMessages = 2,
    DeleteConversation = 3,
    Verify = 4
}
