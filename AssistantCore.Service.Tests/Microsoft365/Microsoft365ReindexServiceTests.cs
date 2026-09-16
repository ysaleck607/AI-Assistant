using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Queries;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Authentication;
using AssistantCore.Service.Application.Services.Microsoft365;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantCore.Service.Tests.Microsoft365;

public sealed class Microsoft365ReindexServiceTests
{
    [Theory, AutoDomainData]
    public async Task Given_TwoEnabledLibraries_When_RequestReindexAsync_Then_CreatesOneSynchronizationPerLibrary(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var firstLibrary = CreateLibrary(organizationId, connection.Id);
        var secondLibrary = CreateLibrary(organizationId, connection.Id);
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(
            organizationId,
            connection,
            [firstLibrary, secondLibrary],
            reindexRepository,
            operatorId,
            now);

        // When
        var response = await service.RequestReindexAsync(organizationId, " reprise ", cancellationToken);

        // Then
        Assert.Equal("Pending", response.Status);
        Assert.Equal(2, response.LibraryCount);
        Assert.Equal(now, response.RequestedAt);
        Assert.Equal(organizationId, response.OrganizationId);
        var created = Assert.Single(reindexRepository.CreatedOperations);
        Assert.Equal(response.OperationId, created.Operation.Id);
        Assert.Equal(connection.Id, created.Operation.Microsoft365ConnectionId);
        Assert.Equal(operatorId, created.Operation.RequestedByOperatorId);
        Assert.Equal("reprise", created.Operation.Reason);
        Assert.Equal(2, created.Operation.SourceCount);
        Assert.Equal(Microsoft365ReindexOperationStatus.Pending, created.Operation.Status);
        Assert.Equal([firstLibrary.Id, secondLibrary.Id], created.SourceIds);
        Assert.Equal(cancellationToken, reindexRepository.ReceivedCancellationToken);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARequestedReindex_When_RequestReindexAsync_Then_StagesTheOperatorAuditEntry(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var library = CreateLibrary(organizationId, connection.Id);
        var auditRepository = new StubAdministrativeAuditRepository();
        var service = CreateService(
            organizationId,
            connection,
            [library],
            new StubReindexOperationRepository(),
            operatorId,
            now,
            auditRepository);

        // When
        var response = await service.RequestReindexAsync(organizationId, "connector fixed", cancellationToken);

        // Then
        var entry = Assert.Single(auditRepository.StagedEntries);
        Assert.Equal(AdministrativeAuditAction.Microsoft365ReindexRequested, entry.Action);
        Assert.Equal(organizationId, entry.OrganizationId);
        Assert.Equal(operatorId, entry.ActorId);
        Assert.Equal("ManagementAdmin", entry.ActorType);
        Assert.Equal("Microsoft365Connection", entry.TargetType);
        Assert.Equal(connection.Id, entry.TargetId);
        Assert.Equal(now, entry.OccurredAt);
        Assert.Contains(response.OperationId.ToString(), entry.NewValues);
        Assert.Contains("connector fixed", entry.NewValues);
    }

    [Theory, AutoDomainData]
    public async Task Given_ARunningReindex_When_RequestReindexAsync_Then_ThrowsConflictWithoutCreatingWork(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var reindexRepository = new StubReindexOperationRepository
        {
            ActiveOperation = new Microsoft365ReindexOperation
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Status = Microsoft365ReindexOperationStatus.Running
            }
        };
        var service = CreateService(
            organizationId,
            connection,
            [CreateLibrary(organizationId, connection.Id)],
            reindexRepository,
            operatorId,
            now);

        // When
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => service.RequestReindexAsync(organizationId, null, cancellationToken));

        // Then
        Assert.Equal(ConflictException.Microsoft365ReindexAlreadyRunning, exception.ErrorCode);
        Assert.Empty(reindexRepository.CreatedOperations);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnUnknownOrganization_When_RequestReindexAsync_Then_ThrowsNotFoundWithoutCreatingWork(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(
            organizationId,
            connection: null,
            libraries: [],
            reindexRepository,
            operatorId,
            now,
            organizationExists: false);

        // When
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => service.RequestReindexAsync(organizationId, null, cancellationToken));

        // Then
        Assert.Equal("Organization not found.", exception.Message);
        Assert.Empty(reindexRepository.CreatedOperations);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoActiveConnection_When_RequestReindexAsync_Then_ThrowsNotFoundWithoutCreatingWork(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(
            organizationId,
            connection: null,
            libraries: [],
            reindexRepository,
            operatorId,
            now);

        // When
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => service.RequestReindexAsync(organizationId, null, cancellationToken));

        // Then
        Assert.Equal("Active Microsoft 365 connection was not found.", exception.Message);
        Assert.Empty(reindexRepository.CreatedOperations);
    }

    [Theory, AutoDomainData]
    public async Task Given_NoEnabledLibrary_When_RequestReindexAsync_Then_ThrowsBadRequestWithoutCreatingWork(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(organizationId, connection, [], reindexRepository, operatorId, now);

        // When
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => service.RequestReindexAsync(organizationId, null, cancellationToken));

        // Then
        Assert.Equal(
            "The organization has no SharePoint library enabled for indexing.",
            exception.Message);
        Assert.Empty(reindexRepository.CreatedOperations);
    }

    [Theory, AutoDomainData]
    public async Task Given_ALibraryOfAnotherConnection_When_RequestReindexAsync_Then_ExcludesItFromTheOperation(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var ownLibrary = CreateLibrary(organizationId, connection.Id);
        var foreignLibrary = CreateLibrary(organizationId, Guid.NewGuid());
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(
            organizationId,
            connection,
            [ownLibrary, foreignLibrary],
            reindexRepository,
            operatorId,
            now);

        // When
        var response = await service.RequestReindexAsync(organizationId, null, cancellationToken);

        // Then
        Assert.Equal(1, response.LibraryCount);
        var created = Assert.Single(reindexRepository.CreatedOperations);
        Assert.Equal([ownLibrary.Id], created.SourceIds);
    }

    [Theory, AutoDomainData]
    public async Task Given_AReasonLongerThanTheLimit_When_RequestReindexAsync_Then_ThrowsBadRequest(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var service = CreateService(
            organizationId,
            connection,
            [CreateLibrary(organizationId, connection.Id)],
            new StubReindexOperationRepository(),
            operatorId,
            now);

        // When
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => service.RequestReindexAsync(organizationId, new string('a', 501), cancellationToken));

        // Then
        Assert.Equal("The reindex reason cannot exceed 500 characters.", exception.Message);
    }

    [Theory]
    [InlineAutoDomainData("")]
    [InlineAutoDomainData("   ")]
    public async Task Given_AnEmptyReason_When_RequestReindexAsync_Then_StoresNoReason(
        string reason,
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given
        var connection = CreateConnection(organizationId);
        var reindexRepository = new StubReindexOperationRepository();
        var service = CreateService(
            organizationId,
            connection,
            [CreateLibrary(organizationId, connection.Id)],
            reindexRepository,
            operatorId,
            now);

        // When
        await service.RequestReindexAsync(organizationId, reason, cancellationToken);

        // Then
        var created = Assert.Single(reindexRepository.CreatedOperations);
        Assert.Null(created.Operation.Reason);
    }

    [Theory, AutoDomainData]
    public async Task Given_AConcurrentReindexWinningTheRace_When_RequestReindexAsync_Then_ThrowsConflict(
        Guid organizationId,
        Guid operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Given : la verification prealable ne voit aucune reprise active, mais la base refuse
        // l'insertion parce qu'un autre operateur a confirme au meme instant.
        var connection = CreateConnection(organizationId);
        var reindexRepository = new StubReindexOperationRepository { RejectsCreation = true };
        var service = CreateService(
            organizationId,
            connection,
            [CreateLibrary(organizationId, connection.Id)],
            reindexRepository,
            operatorId,
            now);

        // When
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => service.RequestReindexAsync(organizationId, null, cancellationToken));

        // Then
        Assert.Equal(ConflictException.Microsoft365ReindexAlreadyRunning, exception.ErrorCode);
    }

    private static Microsoft365ReindexService CreateService(
        Guid organizationId,
        Microsoft365Connection? connection,
        IReadOnlyCollection<Microsoft365Drive> libraries,
        StubReindexOperationRepository reindexRepository,
        Guid operatorId,
        DateTimeOffset now,
        StubAdministrativeAuditRepository? auditRepository = null,
        bool organizationExists = true) =>
        new(
            new StubBackofficeOrganizationQueries(organizationExists ? organizationId : null),
            new StubConnectionRepository(connection),
            new StubDriveRepository(libraries),
            reindexRepository,
            auditRepository ?? new StubAdministrativeAuditRepository(),
            new StubCurrentIdentity(operatorId),
            new StubCorrelationIdProvider("test-correlation"),
            new FixedTimeProvider(now),
            NullLogger<Microsoft365ReindexService>.Instance);

    private static Microsoft365Connection CreateConnection(Guid organizationId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = Microsoft365ConnectionStatus.Active,
            TenantId = "tenant-1"
        };

    private static Microsoft365Drive CreateLibrary(Guid organizationId, Guid connectionId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Microsoft365ConnectionId = connectionId,
            Kind = Microsoft365SourceKind.SharePointDrive,
            DriveId = "drive-" + Guid.NewGuid().ToString("N"),
            IsIndexed = true,
            Status = Microsoft365SourceStatus.Enabled
        };

    private sealed class StubBackofficeOrganizationQueries(Guid? knownOrganizationId)
        : IBackofficeOrganizationQueries
    {
        public Task<BackofficeOrganizationListPageData> SearchOrganizationsAsync(
            int page,
            int pageSize,
            string? search,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BackofficeOrganizationDetailsData?> GetOrganizationDetailsAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(knownOrganizationId == organizationId
                ? new BackofficeOrganizationDetailsData(
                    new BackofficeOrganizationData(
                        organizationId,
                        "MetalPro",
                        "tenant-1",
                        RecordStatus.Active,
                        DateTimeOffset.UnixEpoch),
                    new BackofficeOrganizationUsersData(1, 1),
                    new BackofficeOrganizationMicrosoftData(true, true),
                    new BackofficeOrganizationSourcesData(1, 0),
                    new BackofficeOrganizationIndexingData(0, null, "Healthy"))
                : null);
    }

    private sealed class StubConnectionRepository(Microsoft365Connection? activeConnection)
        : IMicrosoft365ConnectionRepository
    {
        public Task<Microsoft365Connection?> FindActiveByOrganizationAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(activeConnection);

        public Task<Microsoft365Connection> PrepareConsentAsync(
            Guid organizationId,
            string stateHash,
            DateTimeOffset stateExpiresAt,
            DateTimeOffset now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Microsoft365Connection?> FindConsentAsync(
            Guid organizationId,
            string stateHash,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsTenantConnectedToAnotherOrganizationAsync(
            Guid organizationId,
            string tenantId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Microsoft365Connection?> FindByIdAsync(
            Guid connectionId,
            Guid organizationId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Microsoft365Connection?> FindForProcessingAsync(
            Guid connectionId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CompleteConsentAsync(
            Microsoft365Connection connection,
            string tenantId,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task MarkConsentErrorAsync(
            Microsoft365Connection connection,
            string errorCode,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RevokeAsync(
            Microsoft365Connection connection,
            DateTimeOffset occurredAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubDriveRepository(IReadOnlyCollection<Microsoft365Drive> drives)
        : IMicrosoft365DriveRepository
    {
        public Task<IReadOnlyCollection<Microsoft365Drive>> GetIndexedSharePointDrivesAsync(
            Guid organizationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<Microsoft365Drive>>(
                drives.Where(drive => drive.OrganizationId == organizationId).ToArray());

        public Task<Microsoft365Drive?> FindAsync(
            Guid organizationId,
            string driveId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyCollection<Microsoft365Drive>> GetByOwnerAsync(
            Guid organizationId,
            string ownerUserObjectId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Microsoft365Drive> SaveOneDriveAsync(
            Microsoft365Connection connection,
            string driveId,
            string ownerUserObjectId,
            string? ownerUserPrincipalName,
            string displayName,
            string? webUrl,
            DateTimeOffset discoveredAt,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubAdministrativeAuditRepository : IAdministrativeAuditRepository
    {
        public List<AdministrativeAuditEntry> StagedEntries { get; } = [];

        public void Stage(AdministrativeAuditEntry entry) => StagedEntries.Add(entry);

        public Task PersistAsync(
            AdministrativeAuditEntry entry,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class StubCurrentIdentity(Guid operatorId) : ICurrentIdentity
    {
        public AuthenticatedIdentity GetIdentity() =>
            new(
                IdentityProvider.MicrosoftEntraId,
                "external-organization",
                operatorId.ToString(),
                "Synaptix Operator",
                "operator@synaptix.test",
                ["AssistantCore.Management.Admin"]);
    }

    private sealed class StubCorrelationIdProvider(string correlationId) : ICorrelationIdProvider
    {
        public string GetCorrelationId() => correlationId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
