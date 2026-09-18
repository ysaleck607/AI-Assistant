namespace AssistantCore.Repository.Persistence;

/// <summary>
/// Calcule un index aveugle pour un email chiffre au repos : un hachage keye, jamais
/// reversible, qui permet une recherche exacte sans jamais reveler l'email en clair a
/// partir de la seule base. La cle vit hors SQL Server ; ce n'est jamais un simple
/// hachage sans secret, qu'un attaquant pourrait precalculer pour des emails courants.
/// </summary>
public interface IEmailBlindIndexHasher
{
    string ComputeHash(string email);
}
