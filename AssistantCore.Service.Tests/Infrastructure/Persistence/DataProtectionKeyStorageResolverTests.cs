using AssistantCore.Service.Infrastructure.Persistence;

namespace AssistantCore.Service.Tests.Infrastructure.Persistence;

public sealed class DataProtectionKeyStorageResolverTests
{
    private const string BlobUri = "https://assistantcertkeys01.blob.core.windows.net/dataprotection-keys/keys.xml";
    private const string KeyUri = "https://kv-assistant-cert-onp01.vault.azure.net/keys/dataprotection-key/abc123";

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
    public void Given_BothUris_When_Resolve_Then_ReturnsTheSharedKeyStorage(bool _)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = BlobUri,
            KeyVaultKeyUri = KeyUri
        };

        // When
        var keyStorage = DataProtectionKeyStorageResolver.Resolve(options);

        // Then
        Assert.True(keyStorage.IsConfigured);
        Assert.Equal(new Uri(BlobUri), keyStorage.BlobStorageUri);
        Assert.Equal(new Uri(KeyUri), keyStorage.KeyVaultKeyUri);
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
        var options = new DataProtectionKeyStorageOptions { KeyVaultKeyUri = KeyUri };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("BlobStorageUri", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineAutoDomainData("dataprotection-keys/keys.xml")]
    [InlineAutoDomainData("not a uri")]
    public void Given_ARelativeBlobUri_When_Resolve_Then_ThrowsOnTheAbsoluteUriRequirement(
        string blobStorageUri)
    {
        // Given
        var options = new DataProtectionKeyStorageOptions
        {
            BlobStorageUri = blobStorageUri,
            KeyVaultKeyUri = KeyUri
        };

        // When
        var exception = Assert.Throws<InvalidOperationException>(
            () => DataProtectionKeyStorageResolver.Resolve(options));

        // Then
        Assert.Contains("absolute URI", exception.Message, StringComparison.Ordinal);
    }
}
