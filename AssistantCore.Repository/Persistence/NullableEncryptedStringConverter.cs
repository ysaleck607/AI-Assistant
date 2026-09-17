using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssistantCore.Repository.Persistence;

public sealed class NullableEncryptedStringConverter(IFieldEncryptor encryptor)
    : ValueConverter<string?, string?>(
        plaintext => plaintext == null ? null : encryptor.Protect(plaintext),
        storedValue => storedValue == null ? null : encryptor.Unprotect(storedValue));
