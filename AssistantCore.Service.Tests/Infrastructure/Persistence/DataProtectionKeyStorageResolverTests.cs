using AssistantCore.Service.Infrastructure.Persistence;

namespace AssistantCore.Service.Tests.Infrastructure.Persistence;

public sealed class DataProtectionKeyStorageResolverTests
{
    private const string BlobUri = "https://assistantcertkeys01.blob.core.windows.net/dataprotection-keys/keys.xml";
    private const string VersionedKeyUri = "https://kv-assistant-cert-onp01.vault.azure.net/keys/dataprotection-key/abc123";
    private const string VersionlessKeyUri = "https://kv-assistant-cert-onp01.vault.azure.net/keys/dataprotection-key";

    [Theory]
    [InlineAutoDomainData("", "")]
    [InlineAutoDomainData("   ", "   ")]
    public void Given_NoConfiguredUri_When_Resolve_Then_KeepsTheLocalKeyStorage(
        string blobStorageUri,
        string keyVaultKeyUri)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = blobStorageUri,
            KeyVaultKeyUri = keyVaultKeyUri
        };

        // When
        var keyStorage = DataProtectionKeyStorageResolver.Resolve(options);

        // Then
        Assert.False(keyStorage.IsConfigured);
        Assert.Null(keyStorage.BlobStorageUri);
        Assert.Null(keyStorage.KeyVaultKeyUri);
    }

    [Theory, AutoDomainData]
    public void Given_AVersionedKeyUri_When_Resolve_Then_ReturnsTheVersionlessKeyUriForRotation(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = VersionedKeyUri
        };

        // When
        var keyStorage = DataProtectionKeyStorageResolver.Resolve(options);

        // Then
        Assert.True(keyStorage.IsConfigured);
        Assert.Equal(new Uri(BlobUri), keyStorage.BlobStorageUri);
        Assert.Equal(new Uri(VersionlessKeyUri), keyStorage.KeyVaultKeyUri);
    }

    [Theory, AutoDomainData]
    public void Given_AVersionlessKeyUri_When_Resolve_Then_KeepsTheVersionlessKeyUri(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = VersionlessKeyUri
        };

        // When
        var keyStorage = DataProtectionKeyStorageResolver.Resolve(options);

        // Then
        Assert.Equal(new Uri(VersionlessKeyUri), keyStorage.KeyVaultKeyUri);
    }

    [Theory, AutoDomainData]
    public void Given_OnlyTheBlobUri_When_Resolve_Then_ThrowsBecauseTheKeyringWouldNotBeProtected(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions { BlobStorageUri = BlobUri };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("KeyVaultKeyUri", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_OnlyTheKeyUri_When_Resolve_Then_ThrowsBecauseTheKeyringWouldNotBeShared(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions { KeyVaultKeyUri = VersionedKeyUri };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("BlobStorageUri", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineAutoDomainData("dataprotection-keys/keys.xml")]
    [InlineAutoDomainData("not a uri")]
    [InlineAutoDomainData("http://assistantcertkeys01.blob.core.windows.net/dataprotection-keys/keys.xml")]
    public void Given_AnInvalidBlobUri_When_Resolve_Then_ThrowsOnTheHttpsRequirement(
        string blobStorageUri)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = blobStorageUri,
            KeyVaultKeyUri = VersionedKeyUri
        };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("absolute HTTPS URI", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_AnHttpKeyVaultUri_When_Resolve_Then_Throws(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = "http://kv-assistant-cert-onp01.vault.azure.net/keys/dataprotection-key/abc123"
        };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("absolute HTTPS URI", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_AUriThatDoesNotReferenceAKey_When_Resolve_Then_Throws(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = "https://kv-assistant-cert-onp01.vault.azure.net/secrets/not-a-key"
        };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("/keys/{key-name}", exception.Message, StringComparison.Ordinal);
    }

    [Theory, AutoDomainData]
    public void Given_AKeyUriWithExtraSegments_When_Resolve_Then_Throws(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = "https://kv-assistant-cert-onp01.vault.azure.net/keys/dataprotection-key/abc123/extra"
        };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("/keys/{key-name}", exception.Message, StringComparison.Ordinal);
    }
}
