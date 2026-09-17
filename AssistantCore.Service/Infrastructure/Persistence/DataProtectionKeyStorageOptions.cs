namespace AssistantCore.Service.Infrastructure.Persistence;

/// <summary>
/// Emplacement du trousseau de cles de chiffrement applicatif. Absent en local, ou les
/// cles vivent sur le disque du poste; obligatoire des qu'une instance peut etre
/// remplacee ou dupliquee, sinon un redemarrage rend illisible ce qui a ete chiffre.
/// </summary>
public sealed class DataProtectionKeyStorageOptions
{
    public const string SectionName = "DataProtectionKeyStorage";

    /// <summary>
    /// URI du blob qui contient le trousseau, par exemple
    /// <c>https://compte.blob.core.windows.net/dataprotection-keys/keys.xml</c>.
    /// </summary>
    public string BlobStorageUri { get; init; } = string.Empty;

    /// <summary>
    /// URI complete, version comprise, de la cle Key Vault qui chiffre le trousseau.
    /// </summary>
    public string KeyVaultKeyUri { get; init; } = string.Empty;
}
