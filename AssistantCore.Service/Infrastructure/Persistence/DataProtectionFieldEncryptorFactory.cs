using AssistantCore.Repository.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace AssistantCore.Service.Infrastructure.Persistence;

public sealed class DataProtectionFieldEncryptorFactory(IDataProtectionProvider dataProtectionProvider)
    : IFieldEncryptorFactory
{
    public IFieldEncryptor CreateFor(string purpose) =>
        new DataProtectionFieldEncryptor(dataProtectionProvider.CreateProtector(purpose));
}
