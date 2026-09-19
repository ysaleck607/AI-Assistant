using System.Security.Cryptography;
using AssistantCore.Service.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Tests.Infrastructure.Persistence;

public sealed class DataProtectionFieldEncryptorTests
{
    [Theory, AutoDomainData]
    public void Given_APlaintextValue_When_ProtectThenUnprotect_Then_ReturnsTheOriginalValue(string plaintext)
    {
        // Given
        var encryptor = CreateEncryptor();

        // When
        var protectedValue = encryptor.Protect(plaintext);
        var restored = encryptor.Unprotect(protectedValue);

        // Then
        Assert.StartsWith("enc:v1:", protectedValue, StringComparison.Ordinal);
        Assert.NotEqual(plaintext, protectedValue);
        Assert.Equal(plaintext, restored);
    }

    [Theory, AutoDomainData]
    public void Given_ALegacyPlaintextValue_When_Unprotect_Then_ReturnsItUnchangedInstead(string legacyPlaintext)
    {
        // Given
        var encryptor = CreateEncryptor();

        // When
        var restored = encryptor.Unprotect(legacyPlaintext);

        // Then
        Assert.Equal(legacyPlaintext, restored);
    }

    [Theory]
    [InlineAutoDomainData("enc:plain-text")]
    [InlineAutoDomainData("enc:value:that-is-not-versioned")]
    public void Given_ALegacyPlaintextStartingWithEnc_When_Unprotect_Then_ReturnsItUnchanged(
        string legacyPlaintext)
    {
        // Given
        var encryptor = CreateEncryptor();

        // When
        var restored = encryptor.Unprotect(legacyPlaintext);

        // Then
        Assert.Equal(legacyPlaintext, restored);
    }

    [Theory, AutoDomainData]
    public void Given_ALegacyUnversionedCiphertext_When_Unprotect_Then_DecryptsIt(string plaintext)
    {
        // Given
        var provider = CreateProvider();
        var protector = provider.CreateProtector("AssistantCore.Tests.Purpose.v1");
        var legacyCiphertext = protector.Protect(plaintext);
        var encryptor = new DataProtectionFieldEncryptor(protector);

        // When
        var restored = encryptor.Unprotect(legacyCiphertext);

        // Then
        Assert.Equal(plaintext, restored);
    }

    [Theory, AutoDomainData]
    public void Given_TwoDifferentPurposes_When_ProtectTheSameValue_Then_ProducesDifferentCiphertext(string plaintext)
    {
        // Given
        var provider = CreateProvider();
        var first = new DataProtectionFieldEncryptorFactory(provider).CreateFor("purpose-one");
        var second = new DataProtectionFieldEncryptorFactory(provider).CreateFor("purpose-two");

        // When
        var firstProtected = first.Protect(plaintext);
        var secondProtected = second.Protect(plaintext);

        // Then
        Assert.NotEqual(firstProtected, secondProtected);
        Assert.Equal(plaintext, first.Unprotect(firstProtected));
        Assert.Equal(plaintext, second.Unprotect(secondProtected));
    }

    [Theory, AutoDomainData]
    public void Given_TwoOrganizations_When_UnprotectWithTheWrongOrganization_Then_Throws(
        string plaintext,
        Guid firstOrganizationId,
        Guid secondOrganizationId)
    {
        // Given
        var provider = CreateProvider();
        var first = new DataProtectionFieldEncryptorFactory(provider)
            .CreateFor("tenant-sensitive-field", firstOrganizationId);
        var second = new DataProtectionFieldEncryptorFactory(provider)
            .CreateFor("tenant-sensitive-field", secondOrganizationId);
        var protectedValue = first.Protect(plaintext);

        // When
        var action = () => second.Unprotect(protectedValue);

        // Then
        Assert.Throws<CryptographicException>(action);
    }

    [Theory, AutoDomainData]
    public void Given_AnEmptyOrganizationId_When_CreateFor_Then_Throws(bool _)
    {
        // Given
        var factory = new DataProtectionFieldEncryptorFactory(CreateProvider());

        // When
        var action = () => factory.CreateFor("tenant-sensitive-field", Guid.Empty);

        // Then
        var exception = Assert.Throws<ArgumentException>(action);
        Assert.Equal("organizationId", exception.ParamName);
    }

    [Theory, AutoDomainData]
    public void Given_ATamperedVersionedCiphertext_When_Unprotect_Then_Throws(string plaintext)
    {
        // Given
        var encryptor = CreateEncryptor();
        var protectedValue = encryptor.Protect(plaintext);
        var characters = protectedValue.ToCharArray();
        var index = "enc:v1:".Length + ((protectedValue.Length - "enc:v1:".Length) / 2);
        characters[index] = characters[index] == 'A' ? 'B' : 'A';
        var tamperedValue = new string(characters);

        // When
        var action = () => encryptor.Unprotect(tamperedValue);

        // Then
        Assert.Throws<CryptographicException>(action);
    }

    [Theory]
    [InlineAutoDomainData("enc:v2:payload")]
    [InlineAutoDomainData("enc:v99:payload")]
    public void Given_AnUnsupportedEncryptedFormat_When_Unprotect_Then_Throws(string storedValue)
    {
        // Given
        var encryptor = CreateEncryptor();

        // When
        var action = () => encryptor.Unprotect(storedValue);

        // Then
        Assert.Throws<CryptographicException>(action);
    }

    [Theory, AutoDomainData]
    public void Given_AValueEncryptedBeforeKeyRotation_When_UnprotectAfterRotation_Then_ReturnsTheOriginalValue(
        string plaintext)
    {
        // Given
        var directory = Directory.CreateTempSubdirectory("assistantcore-dp-");
        try
        {
            using var firstServices = CreatePersistentProvider(directory);
            var firstProvider = firstServices.GetRequiredService<IDataProtectionProvider>();
            var firstEncryptor = new DataProtectionFieldEncryptorFactory(firstProvider)
                .CreateFor("rotation-test");
            var protectedValue = firstEncryptor.Protect(plaintext);

            var keyManager = firstServices.GetRequiredService<IKeyManager>();
            var now = DateTimeOffset.UtcNow;
            keyManager.CreateNewKey(now.AddMinutes(-1), now.AddDays(90));

            using var secondServices = CreatePersistentProvider(directory);
            var secondProvider = secondServices.GetRequiredService<IDataProtectionProvider>();
            var secondEncryptor = new DataProtectionFieldEncryptorFactory(secondProvider)
                .CreateFor("rotation-test");

            // When
            var restored = secondEncryptor.Unprotect(protectedValue);

            // Then
            Assert.Equal(plaintext, restored);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static DataProtectionFieldEncryptor CreateEncryptor() =>
        new(CreateProvider().CreateProtector("AssistantCore.Tests.Purpose.v1"));

    private static IDataProtectionProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    private static ServiceProvider CreatePersistentProvider(DirectoryInfo directory)
    {
        var services = new ServiceCollection();
        services.AddDataProtection()
            .SetApplicationName("AssistantCore.Tests")
            .PersistKeysToFileSystem(directory);
        return services.BuildServiceProvider();
    }
}
