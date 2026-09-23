using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365AclReconciliationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_AnUnresolvedAcl_When_RunAsync_Then_ContentIsMadeUnavailable(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string siteId,
        string driveId)
    {
        // Given
        var content = CreateDriveContent(
            organizationId,
            sourceId,
            externalContentId,
            siteId,
            driveId);
        var synchronizationService = new RecordingAclSynchronizationService();
        var service = CreateService(
            content,
            new Microsoft365AclResolution.Unresolved(
                Microsoft365AclResolutionFailureReason.PartialResponse),
            synchronizationService);

        // When
        await service.RunAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, synchronizationService.MarkUnavailableCallCount);
        Assert.Equal(0, synchronizationService.SynchronizeCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AResolvedChangedAcl_When_RunAsync_Then_AclIsSynchronized(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string siteId,
        string driveId,
        string allowedUserId)
    {
        // Given
        var content = CreateDriveContent(
            organizationId,
            sourceId,
            externalContentId,
            siteId,
            driveId);
        var acl = new Microsoft365Acl(
            [allowedUserId],
            [],
            [],
            false,
            false,
            Microsoft365AclInheritance.Unique);
        var synchronizationService = new RecordingAclSynchronizationService();
        var service = CreateService(
            content,
            new Microsoft365AclResolution.ResolvedAcl(acl),
            synchronizationService);

        // When
        await service.RunAsync(CancellationToken.None);

        // Then
        Assert.Equal(1, synchronizationService.SynchronizeCallCount);
        Assert.Same(acl, synchronizationService.ReceivedAcl);
        Assert.Equal(0, synchronizationService.MarkUnavailableCallCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOutlookContent_When_RunAsync_Then_ReconcilesItsMailboxAcl(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string mailboxUserId)
    {
        // Given
        var content = CreateOutlookContent(
            organizationId,
            sourceId,
            externalContentId,
            mailboxUserId);
        var synchronizationService = new RecordingAclSynchronizationService();
        var service = CreateService(
            content,
            new Microsoft365AclResolution.Unresolved(
                Microsoft365AclResolutionFailureReason.PartialResponse),
            synchronizationService);

        // When
        await service.RunAsync(CancellationToken.None);

        // Then
        Assert.NotNull(synchronizationService.ReceivedAcl);
        var acl = synchronizationService.ReceivedAcl!;
        Assert.Equal([mailboxUserId], acl.AllowedEntraUserIds);
        Assert.Empty(acl.AllowedEntraGroupIds);
        Assert.Equal(1, synchronizationService.SynchronizeCallCount);
    }

    private static Microsoft365AclReconciliationService CreateService(
        Microsoft365IndexedContent content,
        Microsoft365AclResolution resolution,
        IMicrosoft365ContentAclSynchronizationService synchronizationService) =>
        new(
            new IndexedContentRepositoryFake(content),
            new AclResolverFake(resolution),
            synchronizationService,
            new SecurityIdentityNormalizerFake(),
            Options.Create(new Microsoft365Options()),
            TimeProvider.System,
            NullLogger<Microsoft365AclReconciliationService>.Instance);

    private static Microsoft365IndexedContent CreateDriveContent(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string siteId,
        string driveId)
    {
        var organization = new Organization
        {
            Id = organizationId,
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            Status = RecordStatus.Active
        };
        return new Microsoft365IndexedContent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365SourceId = sourceId,
            ExternalContentId = externalContentId,
            Organization = organization,
            Microsoft365Source = new Microsoft365Drive
            {
                Id = sourceId,
                OrganizationId = organizationId,
                SiteId = siteId,
                DriveId = driveId
            }
        };
    }

    private static Microsoft365IndexedContent CreateOutlookContent(
        Guid organizationId,
        Guid sourceId,
        string externalContentId,
        string mailboxUserId)
    {
        var organization = new Organization
        {
            Id = organizationId,
            IdentityProvider = IdentityProvider.MicrosoftEntraId,
            Status = RecordStatus.Active
        };
        return new Microsoft365IndexedContent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365SourceId = sourceId,
            ExternalContentId = externalContentId,
            Organization = organization,
            Microsoft365Source = new Microsoft365Source
            {
                Id = sourceId,
                Kind = Microsoft365SourceKind.OutlookMailbox,
                ExternalResourceId = mailboxUserId
            }
        };
    }

    private sealed class AclResolverFake(Microsoft365AclResolution resolution)
        : IMicrosoft365AclResolver
    {
        public Task<Microsoft365AclResolution> ResolveAsync(
            Organization organization,
            Microsoft365ContentReference contentReference,
            CancellationToken cancellationToken = default) => Task.FromResult(resolution);
    }

    private sealed class SecurityIdentityNormalizerFake : IMicrosoft365SecurityIdentityNormalizer
    {
        public string NormalizeEntraUserId(string objectId) => objectId;
        public string NormalizeEntraGroupId(string objectId) => objectId;
        public string NormalizeEntraGroupOwnerId(string objectId) => objectId;
        public string NormalizeSharePointGroupId(string siteId, string sharePointGroupId) => sharePointGroupId;
    }

    private sealed class IndexedContentRepositoryFake(Microsoft365IndexedContent content)
        : IMicrosoft365IndexedContentRepository
    {
        public Task<Microsoft365IndexedContent?> FindAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            CancellationToken cancellationToken = default) => Task.FromResult<Microsoft365IndexedContent?>(content);

        public Task<IReadOnlyCollection<Microsoft365IndexedContent>> GetAclReconciliationCandidatesAsync(
            DateTimeOffset dueAt,
            int maximumResults,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Microsoft365IndexedContent>>([content]);

        public Task RequestAclReconciliationAsync(
            Guid sourceId,
            DateTimeOffset dueAt,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(
            Microsoft365IndexedContent indexedContent,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class RecordingAclSynchronizationService
        : IMicrosoft365ContentAclSynchronizationService
    {
        public int MarkUnavailableCallCount { get; private set; }
        public int SynchronizeCallCount { get; private set; }
        public Microsoft365Acl? ReceivedAcl { get; private set; }

        public Task RegisterAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            IReadOnlyCollection<string> chunkIds,
            string aclFingerprint,
            string? siteUrl,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> MarkUnavailableIfRegisteredAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            CancellationToken cancellationToken = default)
        {
            MarkUnavailableCallCount++;
            return Task.FromResult(true);
        }

        public Task<Microsoft365AclSynchronizationResult> SynchronizeIfRegisteredAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            Microsoft365Acl acl,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Microsoft365AclSynchronizationResult.NotRegistered);

        public Task<Microsoft365AclSynchronizationResult> SynchronizeAsync(
            Guid organizationId,
            Guid sourceId,
            string externalContentId,
            Microsoft365Acl acl,
            CancellationToken cancellationToken = default)
        {
            SynchronizeCallCount++;
            ReceivedAcl = acl;
            return Task.FromResult(Microsoft365AclSynchronizationResult.Updated);
        }
    }
}
