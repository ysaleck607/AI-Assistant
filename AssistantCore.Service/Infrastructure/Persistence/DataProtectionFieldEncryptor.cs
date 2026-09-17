using System.Security.Cryptography;
using AssistantCore.Repository.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace AssistantCore.Service.Infrastructure.Persistence;

public sealed class DataProtectionFieldEncryptor(IDataProtector protector) : IFieldEncryptor
{
    public string Protect(string plaintext) => protector.Protect(plaintext);

    public string Unprotect(string storedValue)
    {
        try
        {
            return protector.Unprotect(storedValue);
        }
        catch (CryptographicException)
        {
            // Transitional fallback for rows written before encryption was enabled.
            // Remove once the historical backfill has completed for this field.
            return storedValue;
        }
    }
}
