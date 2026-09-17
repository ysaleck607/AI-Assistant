using AssistantCore.ExternalServices.Services.Microsoft;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Services.Messages.Connectors.Microsoft365;
using AssistantCore.Service.Application.Services.Microsoft365;
using AssistantCore.Service.Infrastructure.Microsoft365;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365SharePointGroupResolverAdapterTests
{
    [Theory, AutoDomainData]
    public async Task Given_NoMicrosoftGraphProfile_When_ResolveGroupIdsAsync_Then_ReturnsNoGroupsWithoutQueryingSharePoint(
        Guid organizationId,
        string externalTenantId,
        Guid entraUserId)
    {
        // Given
        var resolver = new Microsoft365SharePointGroupResolverAdapter(
            new UnreachableSourceDiscoveryRepository(),
            new NullMicrosoft365UserProfileResolver(),
            new MicrosoftCertificateIdentityClient(),
            new MicrosoftSharePointUserGroupClient(new HttpClient()),
            new UnreachableIdentityNormalizer(),
            new MemoryCache(new MemoryCacheOptions()),
            CreateOptions(),
            NullLogger<Microsoft365SharePointGroupResolverAdapter>.Instance);

        // When
        var groupIds = await resolver.ResolveGroupIdsAsync(
            organizationId,
            externalTenantId,
            entraUserId.ToString("D"),
            CancellationToken.None);

        // Then
        Assert.Empty(groupIds);
    }

    private static IOptions<Microsoft365Options> CreateOptions() =>
        Options.Create(new Microsoft365Options
        {
            SharePointCertificatePath = "certificate.pfx",
            SharePointCertificatePassword = "password",
            SharePointGroupCacheMinutes = 5
        });

    // A deleted guest object or a revoked invitation resolves to no Microsoft Graph
    // profile at all; the adapter must stop there rather than falling back to a
    // borrowed or stale identity for the SharePoint lookup.
    private sealed class NullMicrosoft365UserProfileResolver : IMicrosoft365UserProfileResolver
    {
        public Task<Microsoft365UserProfile?> ResolveAsync(
            string externalTenantId,
            string entraUserId,
            CancellationToken cancellationToken) =>
            Task.FromResult<Microsoft365UserProfile?>(null);
    }

    private sealed class UnreachableSourceDiscoveryRepository : IMicrosoft365SourceDiscoveryRepository
    {
        public Task<IReadOnlyCollection<string>> GetSiteIdsAsync(
            Guid organizationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<Microsoft365Site?> FindSiteAsync(
            Guid organizationId, string siteId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<IReadOnlyCollection<Microsoft365List>> GetListsAsync(
            Guid organizationId, string siteId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<Microsoft365List?> FindListAsync(
            Guid organizationId, string siteId, string listId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<Microsoft365ListIndexingRequestCounts> SaveListActivationAsync(
            Microsoft365List list, DateTimeOffset requestedAt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<Microsoft365ListIndexingRequestCounts> SaveListDeactivationAsync(
            Microsoft365List list, DateTimeOffset requestedAt, bool requestIndexCleanup, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task ReconcileSiteSourcesAsync(
            Microsoft365Site site,
            IReadOnlyCollection<Microsoft365SourceDiscoveryData> drives,
            IReadOnlyCollection<Microsoft365SourceDiscoveryData> lists,
            DateTimeOffset discoveredAt,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public Task<IReadOnlyCollection<Microsoft365SharePointSiteData>> GetIndexedSitesAsync(
            Guid organizationId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");
    }

    private sealed class UnreachableIdentityNormalizer : IMicrosoft365SecurityIdentityNormalizer
    {
        public string NormalizeEntraUserId(string objectId) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public string NormalizeEntraGroupId(string objectId) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public string NormalizeEntraGroupOwnerId(string objectId) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");

        public string NormalizeSharePointGroupId(string siteId, string sharePointGroupId) =>
            throw new InvalidOperationException("Must not be called when no profile is resolved.");
    }
}
