using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AssistantCore.Repository.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace AssistantCore.Service.Infrastructure.Persistence;

public sealed partial class DataProtectionFieldEncryptor(IDataProtector protector) : IFieldEncryptor
{
    private const string CurrentFormatPrefix = "enc:v1:";

    public string Protect(string plaintext) =>
        $"{CurrentFormatPrefix}{protector.Protect(plaintext)}";

    public string Unprotect(string storedValue)
    {
        if (storedValue.StartsWith(CurrentFormatPrefix, StringComparison.Ordinal))
        {
            return protector.Unprotect(storedValue[CurrentFormatPrefix.Length..]);
        }

        if (EncryptedFormatPrefix().IsMatch(storedValue))
        {
            throw new CryptographicException("Unsupported encrypted payload format.");
        }

        try
        {
            // Backward compatibility for values encrypted before the explicit
            // application-level format prefix was introduced.
            return protector.Unprotect(storedValue);
        }
        catch (CryptographicException)
        {
            // Transitional fallback for rows written before encryption was enabled.
            // Only explicit versioned encrypted payloads are reserved, so a legacy
            // plaintext such as "enc:example" remains readable.
            return storedValue;
        }
    }

    [GeneratedRegex("^enc:v[0-9]+:", RegexOptions.CultureInvariant)]
    private static partial Regex EncryptedFormatPrefix();
}
