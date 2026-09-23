using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AssistantCore.Service.Tests.Repository;

public sealed class Microsoft365IndexedContentRepositoryEncryptionTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnEncryptedTitle_When_FindAvailableByTitleAsync_Then_StillFindsTheMatchingRow(
        Guid organizationId,
        Guid sourceId,
        string siteId,
        string driveId)
    {
        // Given
        var databaseName = Guid.NewGuid().ToString();
        var encryptorFactory = CreateRealEncryptorFactory();
        const string targetTitle = "Rapport financier Q2";

        await using (var writeContext = CreateDbContext(databaseName, encryptorFactory))
        {
            writeContext.Add(CreateOrganization(organizationId));
            writeContext.Add(CreateDriveSource(sourceId, organizationId, siteId, driveId));
            writeContext.Add(CreateIndexedContent(
                organizationId,
                sourceId,
                title: targetTitle,
                isAvailable: true));
            writeContext.Add(CreateIndexedContent(
                organizationId,
                sourceId,
                title: "Un autre document",
                isAvailable: true));
            writeContext.Add(CreateIndexedContent(
                organizationId,
                sourceId,
                title: targetTitle,
                isAvailable: false));
            await writeContext.SaveChangesAsync();
        }

        // When
        await using var readContext = CreateDbContext(databaseName, encryptorFactory);
        var repository = new Microsoft365IndexedContentRepository(readContext);
        var results = await repository.FindAvailableByTitleAsync(
            organizationId,
            targetTitle,
            CancellationToken.None);

        // Then
        var match = Assert.Single(results);
        Assert.Equal(targetTitle, match.Title);
        Assert.True(match.IsAvailable);
    }

    private static Organization CreateOrganization(Guid organizationId) => new()
    {
        Id = organizationId,
        IdentityProvider = IdentityProvider.MicrosoftEntraId,
        Status = RecordStatus.Active
    };

    private static Microsoft365Drive CreateDriveSource(
        Guid sourceId,
        Guid organizationId,
        string siteId,
        string driveId) => new()
    {
        Id = sourceId,
        OrganizationId = organizationId,
        SiteId = siteId,
        DriveId = driveId
    };

    private static Microsoft365IndexedContent CreateIndexedContent(
        Guid organizationId,
        Guid sourceId,
        string title,
        bool isAvailable) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = organizationId,
        Microsoft365SourceId = sourceId,
        ExternalContentId = Guid.NewGuid().ToString(),
        Title = title,
        IsAvailable = isAvailable,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static AssistantCoreDbContext CreateDbContext(
        string databaseName,
        IFieldEncryptorFactory fieldEncryptorFactory)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AssistantCoreDbContext(options, fieldEncryptorFactory);
    }

    private static IFieldEncryptorFactory CreateRealEncryptorFactory()
    {
        var services = new ServiceCollection();
        services.AddDataProtection().UseEphemeralDataProtectionProvider();
        var provider = services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
        return new DataProtectionFieldEncryptorFactory(provider);
    }
}
