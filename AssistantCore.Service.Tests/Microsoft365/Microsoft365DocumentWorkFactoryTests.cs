using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365DocumentWorkFactoryTests
{
    [Theory, AutoDomainData]
    public void Given_TheSameFileVersionTwice_When_Create_Then_UsesSameProcessDeduplicationKey(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, "report.pdf", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(drive, item, createdAt);
        var replayedWork = factory.Create(drive, item, createdAt.AddMinutes(1));

        // Then
        Assert.Equal(Microsoft365DocumentWorkType.ProcessDocument, firstWork.WorkType);
        Assert.Equal(firstWork.DeduplicationKey, replayedWork.DeduplicationKey);
        Assert.Equal(64, firstWork.DeduplicationKey.Length);
        Assert.Equal(
            CreateExpectedDeduplicationKey(
                organizationId,
                drive.DriveId,
                itemId,
                Microsoft365DocumentIndexVersion.Create(eTag)),
            firstWork.DeduplicationKey);
        Assert.Equal("report.pdf", firstWork.Name);
        Assert.Equal(eTag, firstWork.ETag);
    }

    [Theory, AutoDomainData]
    public void Given_TheSameCanonicalDocumentFromOwnerAndSharedView_When_Create_Then_DeduplicatesAcrossSources(
        Guid organizationId,
        Guid ownerSourceId,
        Guid sharedSourceId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        const string ownerDriveId = "owner-drive-id";
        var ownerDrive = CreateDrive(organizationId, ownerSourceId, ownerDriveId);
        var sharedViewDrive = CreateDrive(organizationId, sharedSourceId, "recipient-drive-id");
        var ownerItem = CreateItem(itemId, "Budget-2027.xlsx", eTag, isDeleted: false);
        var sharedItem = CreateItem(itemId, "Budget-2027.xlsx", eTag, isDeleted: false) with
        {
            CanonicalDriveId = ownerDriveId
        };
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var ownerWork = factory.Create(ownerDrive, ownerItem, createdAt);
        var sharedWork = factory.Create(sharedViewDrive, sharedItem, createdAt.AddSeconds(1));

        // Then
        Assert.Equal(ownerDriveId, ownerWork.DriveId);
        Assert.Equal(ownerDriveId, sharedWork.DriveId);
        Assert.Equal(ownerWork.DriveItemId, sharedWork.DriveItemId);
        Assert.Equal(ownerWork.DeduplicationKey, sharedWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_TheSameCanonicalDocumentInTwoOrganizations_When_Create_Then_DoesNotDeduplicateAcrossTenants(
        Guid firstOrganizationId,
        Guid secondOrganizationId,
        Guid firstSourceId,
        Guid secondSourceId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        const string canonicalDriveId = "same-drive-id";
        var firstDrive = CreateDrive(firstOrganizationId, firstSourceId, canonicalDriveId);
        var secondDrive = CreateDrive(secondOrganizationId, secondSourceId, canonicalDriveId);
        var item = CreateItem(itemId, "Budget-2027.xlsx", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(firstDrive, item, createdAt);
        var secondWork = factory.Create(secondDrive, item, createdAt);

        // Then
        Assert.NotEqual(firstWork.DeduplicationKey, secondWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_AChangedDocumentETag_When_Create_Then_UsesANewProcessDeduplicationKey(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        string firstETag,
        string secondETag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(
            drive,
            CreateItem(itemId, "report.pdf", firstETag, isDeleted: false),
            createdAt);
        var modifiedWork = factory.Create(
            drive,
            CreateItem(itemId, "report.pdf", secondETag, isDeleted: false),
            createdAt.AddMinutes(1));

        // Then
        Assert.NotEqual(firstWork.DeduplicationKey, modifiedWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_ADocumentAlreadyProcessedOutsideAReindex_When_Create_Then_UsesANewKeyForTheReindex(
        Guid organizationId,
        Guid sourceId,
        Guid reindexOperationId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, "report.pdf", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var previousWork = factory.Create(drive, item, createdAt);
        var reindexedWork = factory.Create(drive, item, createdAt.AddMinutes(1), reindexOperationId);

        // Then
        Assert.NotEqual(previousWork.DeduplicationKey, reindexedWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_TheSameDocumentTwiceInOneReindex_When_Create_Then_UsesTheSameKey(
        Guid organizationId,
        Guid sourceId,
        Guid reindexOperationId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, "report.pdf", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(drive, item, createdAt, reindexOperationId);
        var replayedWork = factory.Create(drive, item, createdAt.AddMinutes(1), reindexOperationId);

        // Then
        Assert.Equal(firstWork.DeduplicationKey, replayedWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_TwoDifferentReindexOperations_When_Create_Then_UsesADifferentKeyPerOperation(
        Guid organizationId,
        Guid sourceId,
        Guid firstOperationId,
        Guid secondOperationId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, "report.pdf", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(drive, item, createdAt, firstOperationId);
        var secondWork = factory.Create(drive, item, createdAt, secondOperationId);

        // Then
        Assert.NotEqual(firstWork.DeduplicationKey, secondWork.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_NoReindexOperation_When_Create_Then_KeepsTheHistoricalKeyFormat(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        string eTag,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, "report.pdf", eTag, isDeleted: false);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var work = factory.Create(drive, item, createdAt, reindexOperationId: null);

        // Then
        Assert.Equal(
            CreateExpectedDeduplicationKey(
                organizationId,
                drive.DriveId,
                itemId,
                Microsoft365DocumentIndexVersion.Create(eTag)),
            work.DeduplicationKey);
    }

    [Theory, AutoDomainData]
    public void Given_ADeletedItem_When_Create_Then_CreatesDeleteWorkWithoutDocumentPayload(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, null, null, isDeleted: true);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var work = factory.Create(drive, item, createdAt);

        // Then
        Assert.Equal(Microsoft365DocumentWorkType.DeleteDocument, work.WorkType);
        Assert.Equal(itemId, work.DriveItemId);
        Assert.Null(work.Name);
        Assert.Null(work.ETag);
        Assert.Null(work.MimeType);
    }

    [Theory, AutoDomainData]
    public void Given_TheSameDeletedDocumentTwice_When_Create_Then_UsesSameDeleteDeduplicationKey(
        Guid organizationId,
        Guid sourceId,
        string itemId,
        DateTimeOffset createdAt)
    {
        // Given
        var drive = CreateDrive(organizationId, sourceId);
        var item = CreateItem(itemId, null, null, isDeleted: true);
        var factory = new Microsoft365DocumentWorkFactory();

        // When
        var firstWork = factory.Create(drive, item, createdAt);
        var replayedWork = factory.Create(drive, item, createdAt.AddMinutes(1));

        // Then
        Assert.Equal(firstWork.DeduplicationKey, replayedWork.DeduplicationKey);
    }

    private static Microsoft365Drive CreateDrive(
        Guid organizationId,
        Guid sourceId,
        string driveId = "drive-id") =>
        new()
        {
            Id = sourceId,
            OrganizationId = organizationId,
            SiteId = "site-id",
            DriveId = driveId
        };

    private static Microsoft365DriveItemDelta CreateItem(
        string itemId,
        string? name,
        string? eTag,
        bool isDeleted) =>
        new(
            itemId,
            name,
            eTag,
            DateTimeOffset.Parse("2026-08-01T10:00:00Z"),
            DateTimeOffset.Parse("2026-08-02T11:00:00Z"),
            "https://contoso/document",
            42,
            "application/pdf",
            isDeleted,
            IsFolder: false,
            IsFile: !isDeleted);

    private static string CreateExpectedDeduplicationKey(
        Guid organizationId,
        string driveId,
        string itemId,
        string version)
    {
        var identity = JsonSerializer.Serialize(new[]
        {
            organizationId.ToString("N"),
            driveId,
            itemId,
            version
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
