using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssistantCore.Repository.Persistence;

public sealed class EncryptedStringConverter(IFieldEncryptor encryptor)
    : ValueConverter<string, string>(
        plaintext => encryptor.Protect(plaintext),
        storedValue => encryptor.Unprotect(storedValue));
