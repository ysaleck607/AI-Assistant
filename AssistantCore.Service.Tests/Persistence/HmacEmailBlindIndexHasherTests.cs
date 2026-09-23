using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Persistence;

public sealed class HmacEmailBlindIndexHasherTests
{
    private const string Key = "unit-test-email-lookup-hmac-key-32-chars-min";

    [Fact]
    public void Given_TheSameEmailTwice_When_ComputeHash_Then_ReturnsTheSameHash()
    {
        // Given
        var hasher = CreateHasher();

        // When
        var first = hasher.ComputeHash("Marc.Tremblay@Contoso.test");
        var second = hasher.ComputeHash("Marc.Tremblay@Contoso.test");

        // Then
        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("marc@contoso.test", "MARC@CONTOSO.TEST")]
    [InlineData("marc@contoso.test", "  marc@contoso.test  ")]
    [InlineData("marc@contoso.test", "Marc@Contoso.Test")]
    public void Given_TheSameEmailDifferentlyCasedOrPadded_When_ComputeHash_Then_ReturnsTheSameHash(
        string first,
        string second)
    {
        // Given
        var hasher = CreateHasher();

        // When / Then
        Assert.Equal(hasher.ComputeHash(first), hasher.ComputeHash(second));
    }

    [Fact]
    public void Given_TwoDifferentEmails_When_ComputeHash_Then_ReturnsDifferentHashes()
    {
        // Given
        var hasher = CreateHasher();

        // When
        var first = hasher.ComputeHash("marc@contoso.test");
        var second = hasher.ComputeHash("julie@contoso.test");

        // Then
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Given_TwoDifferentKeys_When_ComputeHash_Then_TheSameEmailProducesDifferentHashes()
    {
        // Given: proves the hash depends on a secret, not just the email - a plain
        // unsalted SHA256(email) would let an attacker precompute common addresses.
        var firstHasher = new HmacEmailBlindIndexHasher(
            Options.Create(new MemberPiiOptions { EmailLookupHmacKey = Key }));
        var secondHasher = new HmacEmailBlindIndexHasher(
            Options.Create(new MemberPiiOptions { EmailLookupHmacKey = "a-completely-different-hmac-key-32-chars-min" }));

        // When
        var first = firstHasher.ComputeHash("marc@contoso.test");
        var second = secondHasher.ComputeHash("marc@contoso.test");

        // Then
        Assert.NotEqual(first, second);
    }

    private static HmacEmailBlindIndexHasher CreateHasher() =>
        new(Options.Create(new MemberPiiOptions { EmailLookupHmacKey = Key }));
}
