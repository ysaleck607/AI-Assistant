namespace AssistantCore.Service.Infrastructure.Persistence;

/// <summary>
/// Emplacement resolu du trousseau de cles. <see cref="IsConfigured"/> distingue le
/// developpement local, ou les cles restent sur le disque, d'un environnement deploye.
/// </summary>
public sealed record DataProtectionKeyStorage(
    bool IsConfigured,
    Uri? BlobStorageUri,
    Uri? KeyVaultKeyUri)
{
    public static DataProtectionKeyStorage NotConfigured { get; } = new(false, null, null);
}

/// <summary>
/// Traduit la configuration en emplacement utilisable, et refuse au demarrage une
/// configuration a moitie faite : une seule des deux valeurs donnerait un trousseau
/// partage mais non chiffre, ou chiffre mais non partage. Les deux cas sont pires que
/// l'absence de configuration, parce qu'ils en donnent l'apparence.
/// </summary>
public static class DataProtectionKeyStorageResolver
{
    public static DataProtectionKeyStorage Resolve(DataProtectionKeyStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var blobStorageUri = options.BlobStorageUri?.Trim() ?? string.Empty;
        var keyVaultKeyUri = options.KeyVaultKeyUri?.Trim() ?? string.Empty;
        var hasBlobStorageUri = blobStorageUri.Length > 0;
        var hasKeyVaultKeyUri = keyVaultKeyUri.Length > 0;

        if (!hasBlobStorageUri && !hasKeyVaultKeyUri)
        {
            return DataProtectionKeyStorage.NotConfigured;
        }

        if (hasBlobStorageUri != hasKeyVaultKeyUri)
        {
            var missing = hasBlobStorageUri
                ? nameof(DataProtectionKeyStorageOptions.KeyVaultKeyUri)
                : nameof(DataProtectionKeyStorageOptions.BlobStorageUri);
            throw new InvalidOperationException(
                $"{DataProtectionKeyStorageOptions.SectionName}:{missing} is required "
                + "as soon as the other value is configured.");
        }

        return new DataProtectionKeyStorage(
            true,
            ParseAbsoluteUri(blobStorageUri, nameof(DataProtectionKeyStorageOptions.BlobStorageUri)),
            ParseAbsoluteUri(keyVaultKeyUri, nameof(DataProtectionKeyStorageOptions.KeyVaultKeyUri)));
    }

    private static Uri ParseAbsoluteUri(string value, string name) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidOperationException(
                $"{DataProtectionKeyStorageOptions.SectionName}:{name} must be an absolute URI.");
}
