namespace AssistantCore.Service.Application.Configuration;

public sealed class MemberPiiOptions
{
    public const string SectionName = "MemberPii";

    /// <summary>
    /// Cle HMAC pour l'index aveugle de l'email des membres. Doit rester stable dans
    /// le temps (contrairement aux cles de chiffrement Data Protection, qui tournent) :
    /// la faire tourner invaliderait tous les index deja calcules. Ne vit jamais en base.
    /// </summary>
    public string EmailLookupHmacKey { get; init; } = string.Empty;
}
