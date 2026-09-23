using AssistantCore.Repository.Persistence;

namespace AssistantCore.Service.Tests.Repository;

public sealed class NullableEncryptedStringConverterTests
{
    [Theory, AutoDomainData]
    public void Given_APlaintextValue_When_ConvertToProvider_Then_DelegatesToEncryptorProtect(string plaintext)
    {
        // Given
        var converter = new NullableEncryptedStringConverter(new ReversingFieldEncryptor());

        // When
        var stored = converter.ConvertToProviderExpression.Compile().Invoke(plaintext);

        // Then
        Assert.Equal(Reverse(plaintext), stored);
    }

    [Theory, AutoDomainData]
    public void Given_AStoredValue_When_ConvertFromProvider_Then_DelegatesToEncryptorUnprotect(string plaintext)
    {
        // Given
        var converter = new NullableEncryptedStringConverter(new ReversingFieldEncryptor());
        var stored = Reverse(plaintext);

        // When
        var restored = converter.ConvertFromProviderExpression.Compile().Invoke(stored);

        // Then
        Assert.Equal(plaintext, restored);
    }

    [Fact]
    public void Given_ANullValue_When_ConvertToProvider_Then_ReturnsNullWithoutCallingTheEncryptor()
    {
        // Given
        var converter = new NullableEncryptedStringConverter(new ThrowingFieldEncryptor());

        // When
        var stored = converter.ConvertToProviderExpression.Compile().Invoke(null);

        // Then
        Assert.Null(stored);
    }

    [Fact]
    public void Given_ANullStoredValue_When_ConvertFromProvider_Then_ReturnsNullWithoutCallingTheEncryptor()
    {
        // Given
        var converter = new NullableEncryptedStringConverter(new ThrowingFieldEncryptor());

        // When
        var restored = converter.ConvertFromProviderExpression.Compile().Invoke(null);

        // Then
        Assert.Null(restored);
    }

    private static string Reverse(string value) => new(value.Reverse().ToArray());

    private sealed class ReversingFieldEncryptor : IFieldEncryptor
    {
        public string Protect(string plaintext) => Reverse(plaintext);

        public string Unprotect(string storedValue) => Reverse(storedValue);
    }

    private sealed class ThrowingFieldEncryptor : IFieldEncryptor
    {
        public string Protect(string plaintext) => throw new InvalidOperationException("Should not be called for null.");

        public string Unprotect(string storedValue) => throw new InvalidOperationException("Should not be called for null.");
    }
}
