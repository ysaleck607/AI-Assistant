namespace AssistantCore.Repository.Persistence;

public interface IFieldEncryptor
{
    string Protect(string plaintext);

    string Unprotect(string storedValue);
}
