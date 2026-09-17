using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Authentication;
using AssistantCore.Service.Application.Services.Backoffice;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Backoffice;

public sealed class BackofficeOrganizationServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_InvalidPagination_When_SearchOrganizationsAsync_Then_NormalizesPagination(
        CancellationToken cancellationToken)
    {
        // Given
        var queries = new StubBackofficeOrganizationQueries
        {
            ListResponse = new BackofficeOrganizationListPageData([], 1, 25, 0)
        };
        var service = CreateService(queries);

        // When
        var result = await service.SearchOrganizationsAsync(0, 0, "metal", cancellationToken);

        // Then
        Assert.Equal(1, result.Page);
        Assert.Equal(25, result.PageSize);
        Assert.Equal(1, queries.ReceivedPage);
        Assert.Equal(25, queries.ReceivedPageSize);
        Assert.Equal("metal", queries.ReceivedSearch);
        Assert.Equal(cancellationToken, queries.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_PageSizeAboveMaximum_When_SearchOrganizationsAsync_Then_CapsPageSize(
        CancellationToken cancellationToken)
    {
        // Given
        var queries = new StubBackofficeOrganizationQueries
        {
            ListResponse = new BackofficeOrganizationListPageData([], 1, 100, 0)
        };
        var service = CreateService(queries);

        // When
        var result = await service.SearchOrganizationsAsync(1, 250, null, cancellationToken);

        // Then
        Assert.Equal(100, result.PageSize);
        Assert.Equal(100, queries.ReceivedPageSize);
        Assert.Equal(cancellationToken, queries.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_ExistingOrganization_When_GetOrganizationDetailsAsync_Then_ReturnsDetails(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        // Given
        var details = CreateOrganizationDetails(organizationId);
        var queries = new StubBackofficeOrganizationQueries { DetailsResponse = details };
        var service = CreateService(queries);

        // When
        var result = await service.GetOrganizationDetailsAsync(organizationId, cancellationToken);

        // Then
        Assert.Equal(organizationId, result.Organization.Id);
        Assert.Equal("MetalPro", result.Organization.Name);
        Assert.Equal("tenant-metal", result.Organization.TenantId);
        Assert.Equal("Active", result.Organization.Status);
        Assert.Equal(3, result.Users.Total);
        Assert.Equal(2, result.Users.Active);
        Assert.True(result.Microsoft.Connected);
        Assert.True(result.Microsoft.AdminConsentGranted);
        Assert.Equal(1, result.Sources.SharePointSiteCount);
        Assert.Equal(1, result.Sources.OneDriveCount);
        Assert.Equal(42, result.Indexing.DocumentCount);
        Assert.Equal("Healthy", result.Indexing.Status);
        Assert.Equal(organizationId, queries.ReceivedOrganizationId);
        Assert.Equal(cancellationToken, queries.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_UserBelongsToAnotherOrganization_When_GetUserDetailsAsync_Then_ThrowsNotFound(
        Guid organizationId,
        Guid otherOrganizationId,
        Guid userId)
    {
        // Given
        var queries = new StubBackofficeOrganizationQueries
        {
            DetailsResponse = CreateOrganizationDetails(organizationId)
        };
        var memberQueries = new StubOrganizationMemberQueries
        {
            Member = new OrganizationMember
            {
                Id = userId,
                OrganizationId = otherOrganizationId,
                Name = "Other tenant user",
                Email = "other@example.test",
                ExternalUserId = Guid.NewGuid().ToString(),
                IdentityProvider = IdentityProvider.MicrosoftEntraId,
                Role = OrganizationRole.User,
                Status = RecordStatus.Active
            }
        };
        var service = CreateService(queries, memberQueries);

        // When
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetUserDetailsAsync(organizationId, userId, CancellationToken.None));

        // Then
        Assert.Equal("User not found in this organization.", exception.Message);
        Assert.Equal(organizationId, memberQueries.ReceivedOrganizationId);
        Assert.Equal(userId, memberQueries.ReceivedMemberId);
    }

    [Theory, AutoDomainData]
    public async Task Given_ValidUser_When_ReevaluateUserAccessAsync_Then_PersistsAuditWithCorrelationId(
        Guid organizationId,
        Guid userId,
        Guid actorId,
        CancellationToken cancellationToken)
    {
        // Given
        const string correlationId = "backoffice-correlation-id";
        var queries = new StubBackofficeOrganizationQueries
        {
            DetailsResponse = CreateOrganizationDetails(organizationId)
        };
        var memberQueries = new StubOrganizationMemberQueries
        {
            Member = new OrganizationMember
            {
                Id = userId,
                OrganizationId = organizationId,
                Name = "User",
                Email = "user@example.test",
                ExternalUserId = Guid.NewGuid().ToString(),
                IdentityProvider = IdentityProvider.MicrosoftEntraId,
                Role = OrganizationRole.User,
                Status = RecordStatus.Active
            }
        };
        var auditRepository = new StubAdministrativeAuditRepository();
        var service = CreateService(
            queries,
            memberQueries,
            auditRepository,
            new StubCurrentIdentity(actorId),
            new StubCorrelationIdProvider(correlationId));

        // When
        var result = await service.ReevaluateUserAccessAsync(
            organizationId,
            userId,
            cancellationToken);

        // Then
        Assert.True(result.Diagnostic.AccessAllowed);
        Assert.Equal(correlationId, result.CorrelationId);
        Assert.NotNull(auditRepository.PersistedEntry);
        Assert.Equal(organizationId, auditRepository.PersistedEntry.OrganizationId);
        Assert.Equal(actorId, auditRepository.PersistedEntry.ActorId);
        Assert.Equal(userId, auditRepository.PersistedEntry.TargetId);
        Assert.Equal(AdministrativeAuditAction.UserAccessReevaluated, auditRepository.PersistedEntry.Action);
        Assert.Equal(correlationId, auditRepository.PersistedEntry.CorrelationId);
        Assert.Equal(cancellationToken, auditRepository.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_InactiveOrganization_When_ReevaluateUserAccessAsync_Then_DeniesWithKnownReason(
        Guid organizationId,
        Guid userId,
        Guid actorId)
    {
        // Given
        var details = CreateOrganizationDetails(organizationId) with
        {
            Organization = new BackofficeOrganizationData(
                organizationId,
                "MetalPro",
                "tenant-metal",
                RecordStatus.Inactive,
                DateTimeOffset.Parse("2026-09-10T20:00:00Z"))
        };
        var queries = new StubBackofficeOrganizationQueries { DetailsResponse = details };
        var memberQueries = new StubOrganizationMemberQueries
        {
            Member = new OrganizationMember
            {
                Id = userId,
                OrganizationId = organizationId,
                Name = "User",
                Email = "user@example.test",
                ExternalUserId = Guid.NewGuid().ToString(),
                IdentityProvider = IdentityProvider.MicrosoftEntraId,
                Role = OrganizationRole.User,
                Status = RecordStatus.Active
            }
        };
        var service = CreateService(
            queries,
            memberQueries,
            new StubAdministrativeAuditRepository(),
            new StubCurrentIdentity(actorId),
            new StubCorrelationIdProvider("correlation"));

        // When
        var result = await service.ReevaluateUserAccessAsync(
            organizationId,
            userId,
            CancellationToken.None);

        // Then
        Assert.False(result.Diagnostic.AccessAllowed);
        Assert.Equal("OrganizationInactive", result.Diagnostic.Code);
        Assert.Contains("L’organisation est inactive.", result.Diagnostic.Reasons);
    }

    [Theory, AutoDomainData]
    public async Task Given_EmptyOrganizationId_When_GetOrganizationDetailsAsync_Then_ThrowsBadRequest(int _)
    {
        // Given
        var service = CreateService(new StubBackofficeOrganizationQueries());

        // When
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => service.GetOrganizationDetailsAsync(Guid.Empty, CancellationToken.None));

        // Then
        Assert.Equal("Organization identifier is required.", exception.Message);
    }

    [Theory, AutoDomainData]
    public async Task Given_MissingOrganization_When_GetOrganizationDetailsAsync_Then_ThrowsNotFound(Guid organizationId)
    {
        // Given
        var service = CreateService(new StubBackofficeOrganizationQueries());

        // When
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => service.GetOrganizationDetailsAsync(organizationId, CancellationToken.None));

        // Then
        Assert.Equal("Organization not found.", exception.Message);
    }

    private static BackofficeOrganizationService CreateService(
        StubBackofficeOrganizationQueries organizationQueries,
        StubOrganizationMemberQueries? memberQueries = null,
        StubAdministrativeAuditRepository? auditRepository = null,
        ICurrentIdentity? currentIdentity = null,
        ICorrelationIdProvider? correlationIdProvider = null) =>
        new(
            organizationQueries,
            memberQueries ?? new StubOrganizationMemberQueries(),
            auditRepository ?? new StubAdministrativeAuditRepository(),
            currentIdentity ?? new StubCurrentIdentity(Guid.NewGuid()),
            correlationIdProvider ?? new StubCorrelationIdProvider("test-correlation"),
            NullLogger<BackofficeOrganizationService>.Instance);

    private static BackofficeOrganizationDetailsData CreateOrganizationDetails(Guid organizationId) =>
        new(
            new BackofficeOrganizationData(
                organizationId,
                "MetalPro",
                "tenant-metal",
                RecordStatus.Active,
                DateTimeOffset.Parse("2026-09-10T20:00:00Z")),
            new BackofficeOrganizationUsersData(3, 2),
            new BackofficeOrganizationMicrosoftData(true, true),
            new BackofficeOrganizationSourcesData(1, 1),
            new BackofficeOrganizationIndexingData(
                42,
                DateTimeOffset.Parse("2026-09-10T21:00:00Z"),
                "Healthy"));

    private sealed class StubBackofficeOrganizationQueries : IBackofficeOrganizationQueries
    {
        public BackofficeOrganizationListPageData? ListResponse { get; init; }
        public BackofficeOrganizationDetailsData? DetailsResponse { get; init; }
        public int ReceivedPage { get; private set; }
        public int ReceivedPageSize { get; private set; }
        public string? ReceivedSearch { get; private set; }
        public Guid ReceivedOrganizationId { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public Task<BackofficeOrganizationListPageData> SearchOrganizationsAsync(
            int page,
            int pageSize,
            string? search,
            CancellationToken cancellationToken = default)
        {
            ReceivedPage = page;
            ReceivedPageSize = pageSize;
            ReceivedSearch = search;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(ListResponse!);
        }

        public Task<BackofficeOrganizationDetailsData?> GetOrganizationDetailsAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default)
        {
            ReceivedOrganizationId = organizationId;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(DetailsResponse);
        }
    }

    private sealed class StubOrganizationMemberQueries : IOrganizationMemberQueries
    {
        public OrganizationMember? Member { get; init; }
        public Guid ReceivedOrganizationId { get; private set; }
        public Guid ReceivedMemberId { get; private set; }

        public Task<IReadOnlyCollection<OrganizationMember>> GetMembers(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<OrganizationMember>>(
                Member is not null && Member.OrganizationId == organizationId ? [Member] : []);

        public Task<OrganizationMember?> FindMember(
            Guid organizationId,
            IdentityProvider identityProvider,
            string externalUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Member is not null
                && Member.OrganizationId == organizationId
                && Member.IdentityProvider == identityProvider
                && Member.ExternalUserId == externalUserId
                    ? Member
                    : null);

        public Task<OrganizationMember?> FindMember(
            Guid organizationId,
            Guid memberId,
            CancellationToken cancellationToken = default)
        {
            ReceivedOrganizationId = organizationId;
            ReceivedMemberId = memberId;
            return Task.FromResult(
                Member is not null && Member.OrganizationId == organizationId && Member.Id == memberId
                    ? Member
                    : null);
        }

        public Task<OrganizationMember> CreateMember(
            OrganizationMember member,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<OrganizationMember> UpdateRole(
            OrganizationMember member,
            OrganizationRole role,
            Guid actorId,
            DateTimeOffset occurredAt,
            string correlationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<MemberUpdateResult> UpdateStatus(
            Guid organizationId,
            Guid memberId,
            RecordStatus status,
            int? expectedVersion,
            Guid actorId,
            DateTimeOffset occurredAt,
            string correlationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RecordSuccessfulAuthenticationAsync(
            Guid memberId,
            DateTimeOffset authenticatedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RefreshContactDetailsAsync(
            Guid memberId,
            string name,
            string email,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubAdministrativeAuditRepository : IAdministrativeAuditRepository
    {
        public AdministrativeAuditEntry? PersistedEntry { get; private set; }
        public CancellationToken ReceivedCancellationToken { get; private set; }

        public void Stage(AdministrativeAuditEntry entry) => throw new NotSupportedException();

        public Task PersistAsync(
            AdministrativeAuditEntry entry,
            CancellationToken cancellationToken = default)
        {
            PersistedEntry = entry;
            ReceivedCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCurrentIdentity(Guid actorId) : ICurrentIdentity
    {
        public AuthenticatedIdentity GetIdentity() =>
            new(
                IdentityProvider.MicrosoftEntraId,
                "management-tenant",
                actorId.ToString(),
                "Platform Admin",
                "admin@example.test",
                ["ManagementAdmin"]);
    }

    private sealed class StubCorrelationIdProvider(string correlationId) : ICorrelationIdProvider
    {
        public string GetCorrelationId() => correlationId;
    }
}
