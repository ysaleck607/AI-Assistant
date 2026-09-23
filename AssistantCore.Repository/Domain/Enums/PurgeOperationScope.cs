namespace AssistantCore.Repository.Domain.Enums;

/// <summary>
/// Categorie de retention purgee par une operation. Conversation n'y figure pas :
/// elle dispose deja de son propre mecanisme dedie (ConversationPurgeRequest).
/// </summary>
public enum PurgeOperationScope
{
    Search = 2,
    Ingestion = 3,
    Logs = 4,
    Audit = 5
}
