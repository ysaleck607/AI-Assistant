namespace AssistantCore.Repository.Repositories.Audit;

/// <summary>
/// Valeurs stables pour les champs actorType/targetType, distinctes de l'action elle-meme
/// puisqu'une meme action peut a terme concerner des types d'acteur ou de cible differents.
/// </summary>
public static class AdministrativeAuditSubjectTypes
{
    public const string Member = "Member";

    public const string Conversation = "Conversation";

    public const string Microsoft365Connection = "Microsoft365Connection";
}
