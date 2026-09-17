using AssistantCore.Service.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
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

    private static DataProtectionFieldEncryptor CreateEncryptor() =>
        new(CreateProvider().CreateProtector("AssistantCore.Tests.Purpose.v1"));

    private static IDataProtectionProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }
}
