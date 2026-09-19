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

        var parsedBlobStorageUri = ParseHttpsUri(
            blobStorageUri,
            nameof(DataProtectionKeyStorageOptions.BlobStorageUri));
        var parsedKeyVaultUri = ParseHttpsUri(
            keyVaultKeyUri,
            nameof(DataProtectionKeyStorageOptions.KeyVaultKeyUri));

        return new DataProtectionKeyStorage(
            true,
            parsedBlobStorageUri,
            ToVersionlessKeyVaultKeyUri(parsedKeyVaultUri));
    }

    private static Uri ParseHttpsUri(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyStorageOptions.SectionName}:{name} must be an absolute HTTPS URI.");
        }

        return uri;
    }

    private static Uri ToVersionlessKeyVaultKeyUri(Uri keyVaultKeyUri)
    {
        var segments = keyVaultKeyUri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if ((segments.Length != 2 && segments.Length != 3)
            || !string.Equals(segments[0], "keys", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(segments[1])
            || (segments.Length == 3 && string.IsNullOrWhiteSpace(segments[2])))
        {
            throw new InvalidOperationException(
                $"{DataProtectionKeyStorageOptions.SectionName}:{nameof(DataProtectionKeyStorageOptions.KeyVaultKeyUri)} "
                + "must reference a Key Vault key URI in the form /keys/{key-name} or /keys/{key-name}/{key-version}.");
        }

        var builder = new UriBuilder(keyVaultKeyUri)
        {
            Path = $"/keys/{segments[1]}",
            Query = string.Empty,
            Fragment = string.Empty
        };

        return builder.Uri;
    }
}
