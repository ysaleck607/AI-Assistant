using AssistantCore.Service.Application.Services.Incidents;

namespace AssistantCore.Service.Tests.Incidents;

public sealed class SensitiveDataRedactorTests
{
    private readonly SensitiveDataRedactor redactor = new();

    [Theory]
    [InlineData(
        "Call failed. Authorization: Bearer abc.def.ghi123456789",
        "Call failed. [REDACTED]")]
    [InlineData(
        "Request header Bearer eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dGhpc19pc19hX2Zha2Vfc2lnbmF0dXJl was rejected",
        "Request header [REDACTED] was rejected")]
    [InlineData(
        "Connection string: Server=tcp:db;Password=SuperSecret123!;Database=Assistant",
        "Connection string: Server=tcp:db;[REDACTED];Database=Assistant")]
    [InlineData(
        "Blob upload failed: AccountKey=abcDEF123+/==;EndpointSuffix=core.windows.net",
        "Blob upload failed: [REDACTED];EndpointSuffix=core.windows.net")]
    [InlineData(
        "SAS rejected: sig=abcdefghijklmnop1234567890&se=2026-01-01",
        "SAS rejected: [REDACTED]&se=2026-01-01")]
    [InlineData(
        "Client secret leaked: client_secret=sup3rSecretValue",
        "Client secret leaked: [REDACTED]")]
    [InlineData(
        "Header x-api-key: 1234567890abcdefghijklmnopqrstuvwxyz",
        "Header [REDACTED]")]
    [InlineData(
        "Opaque token abcDEFghi0123456789ABCDEFGHIJ0123456789xy rejected",
        "Opaque token [REDACTED] rejected")]
    public void Given_TextContainingASecret_When_Redacting_Then_TheSecretIsReplaced(
        string input,
        string expected)
    {
        // When
        var result = redactor.Redact(input);

        // Then
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("The organization was not found.")]
    [InlineData("Timeout while calling the Foundry agent after 30 seconds.")]
    [InlineData("Member 3fa85f64-5717-4562-b3fc-2c963f66afa6 does not have access to this resource.")]
    public void Given_TextWithNoSecret_When_Redacting_Then_TextIsUnchanged(string input)
    {
        // When
        var result = redactor.Redact(input);

        // Then
        Assert.Equal(input, result);
    }

    [Theory]
    [InlineData("Organization Contoso (admin@contoso.com) failed to sync.")]
    public void Given_TextContainingOrganizationOrEmailPii_When_Redacting_Then_PiiIsNotRedacted(string input)
    {
        // Redaction targets secrets, not PII: Summary/SafeDetail are built from exception
        // messages, not from raw member records, so PII scrubbing is out of scope here.

        // When
        var result = redactor.Redact(input);

        // Then
        Assert.Equal(input, result);
    }

    [Fact]
    public void Given_NullOrEmptyText_When_Redacting_Then_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, redactor.Redact(null));
        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
    }
}
