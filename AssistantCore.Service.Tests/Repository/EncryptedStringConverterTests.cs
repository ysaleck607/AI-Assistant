using AssistantCore.Repository.Persistence;

namespace AssistantCore.Service.Tests.Repository;

public sealed class EncryptedStringConverterTests
{
    [Theory, AutoDomainData]
    public void Given_APlaintextValue_When_ConvertToProvider_Then_DelegatesToEncryptorProtect(string plaintext)
    {
        // Given
        var converter = new EncryptedStringConverter(new ReversingFieldEncryptor());

        // When
        var stored = converter.ConvertToProviderExpression.Compile().Invoke(plaintext);

        // Then
        Assert.Equal(Reverse(plaintext), stored);
    }

    [Theory, AutoDomainData]
    public void Given_AStoredValue_When_ConvertFromProvider_Then_DelegatesToEncryptorUnprotect(string plaintext)
    {
        // Given
        var converter = new EncryptedStringConverter(new ReversingFieldEncryptor());
        var stored = Reverse(plaintext);

        // When
        var restored = converter.ConvertFromProviderExpression.Compile().Invoke(stored);

        // Then
        Assert.Equal(plaintext, restored);
    }

    private static string Reverse(string value) => new(value.Reverse().ToArray());

    private sealed class ReversingFieldEncryptor : IFieldEncryptor
    {
        public string Protect(string plaintext) => Reverse(plaintext);

        public string Unprotect(string storedValue) => Reverse(storedValue);
    }
}
