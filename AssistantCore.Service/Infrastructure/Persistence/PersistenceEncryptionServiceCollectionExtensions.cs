using AssistantCore.Repository.Persistence;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Infrastructure.Persistence;

public static class PersistenceEncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Nom partage par l'API et le Worker. Data Protection isole les trousseaux par nom
    /// d'application : deux noms differents produisent deux jeux de cles, et le Worker ne
    /// peut alors plus relire ce que l'API a chiffre.
    /// </summary>
    private const string SharedApplicationName = "AssistantCore";

    public static IServiceCollection AddPersistenceEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var options = configuration
            .GetSection(DataProtectionKeyStorageOptions.SectionName)
            .Get<DataProtectionKeyStorageOptions>() ?? new DataProtectionKeyStorageOptions();
        var keyStorage = DataProtectionKeyStorageResolver.Resolve(options);

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(SharedApplicationName);

        if (keyStorage.IsConfigured)
        {
            // L'identite managee porte les droits sur le conteneur et sur la cle; aucun
            // secret n'est donc necessaire pour atteindre le trousseau.
            var credential = new DefaultAzureCredential();
            dataProtection
                .PersistKeysToAzureBlobStorage(keyStorage.BlobStorageUri!, credential)
                .ProtectKeysWithAzureKeyVault(keyStorage.KeyVaultKeyUri!, credential);
        }

        services.AddSingleton<IFieldEncryptorFactory, DataProtectionFieldEncryptorFactory>();

        return services;
    }
}
