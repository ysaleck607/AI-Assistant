using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AssistantCore.Repository.Persistence;

/// <summary>
/// Includes the context's <see cref="IFieldEncryptorFactory"/> in the model cache key so that
/// two <see cref="AssistantCoreDbContext"/> instances built with different encryptors never
/// share a compiled model (which would silently bake in the wrong encryptor for one of them).
/// </summary>
internal sealed class EncryptionAwareModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), designTime, GetEncryptorFactory(context));

    private static IFieldEncryptorFactory? GetEncryptorFactory(DbContext context) =>
        (context as AssistantCoreDbContext)?.EncryptorFactory;
}
