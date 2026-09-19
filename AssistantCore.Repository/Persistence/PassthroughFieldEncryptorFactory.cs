namespace AssistantCore.Repository.Persistence;

/// <summary>
/// No-op encryptor used when <see cref="AssistantCoreDbContext"/> is constructed directly
/// (e.g. in tests) without going through dependency injection. Production hosts always
/// register a real <see cref="IFieldEncryptorFactory"/> via DI, which takes precedence.
/// </summary>
public sealed class PassthroughFieldEncryptorFactory : IFieldEncryptorFactory
{
    public static readonly PassthroughFieldEncryptorFactory Instance = new();

    public IFieldEncryptor CreateFor(string purpose, Guid? organizationId = null) =>
        PassthroughFieldEncryptor.Instance;

    private sealed class PassthroughFieldEncryptor : IFieldEncryptor
    {
        public static readonly PassthroughFieldEncryptor Instance = new();

        public string Protect(string plaintext) => plaintext;

        public string Unprotect(string storedValue) => storedValue;
    }
}
