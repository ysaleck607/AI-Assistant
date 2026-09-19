using AssistantCore.Repository.Persistence;
using Microsoft.AspNetCore.DataProtection;

namespace AssistantCore.Service.Infrastructure.Persistence;

public sealed class DataProtectionFieldEncryptorFactory(IDataProtectionProvider dataProtectionProvider)
    : IFieldEncryptorFactory
{
    public IFieldEncryptor CreateFor(string purpose, Guid? organizationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        if (organizationId == Guid.Empty)
        {
            throw new ArgumentException("Organization id must be a non-empty GUID when provided.", nameof(organizationId));
        }

        IDataProtector protector = dataProtectionProvider.CreateProtector(purpose);
        if (organizationId is { } tenantId)
        {
            protector = protector.CreateProtector($"organization:{tenantId:D}");
        }

        return new DataProtectionFieldEncryptor(protector);
    }
}
