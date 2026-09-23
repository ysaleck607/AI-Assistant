namespace AssistantCore.Repository.Persistence;

public interface IFieldEncryptorFactory
{
    IFieldEncryptor CreateFor(string purpose, Guid? organizationId = null);
}
