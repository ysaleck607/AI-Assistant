using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using AssistantCore.Repository.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Internal;

namespace AssistantCore.Service.Tests.Repository;

public sealed class PurgeOperationRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Theory, AutoDomainData]
    public async Task Given_ACompetingOperationAlreadyCreated_When_RequestAsync_Then_ReturnsTheExistingOperation(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .Options;

        var winningOperation = await SeedAsync(
            new AssistantCoreDbContext(options), organizationId, PurgeOperationScope.Audit, targetId, Now.AddDays(30));

        var identityConflict = new DbUpdateException(
            "Simulated database conflict.",
            new Exception(
                "Cannot insert duplicate key row with unique index 'IX_PurgeOperation_OrganizationId_Scope_TargetId'."));
        await using var throwingContext = new ThrowOnceDbContext(options, identityConflict);
        var repository = new PurgeOperationRepository(throwingContext);

        var result = await repository.RequestAsync(
            organizationId, PurgeOperationScope.Audit, targetId, Now, Now.AddDays(30), "Requested");

        Assert.Equal(winningOperation.Id, result.Id);
    }

    [Theory, AutoDomainData]
    public async Task Given_TheSameTargetInTwoScopes_When_RequestAsync_Then_CreatesTwoDistinctOperations(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        // A coincidental Guid collision across unrelated retention scopes must never be
        // treated as the same purge target.
        await using var dbContext = CreateDbContext(databaseId);
        var repository = new PurgeOperationRepository(dbContext);

        var searchOperation = await repository.RequestAsync(
            organizationId, PurgeOperationScope.Search, targetId, Now, Now.AddDays(30), "Requested");
        var ingestionOperation = await repository.RequestAsync(
            organizationId, PurgeOperationScope.Ingestion, targetId, Now, Now.AddDays(30), "Requested");

        Assert.NotEqual(searchOperation.Id, ingestionOperation.Id);
    }

    [Theory, AutoDomainData]
    public async Task Given_ADueOperation_When_ClaimNextAsync_Then_TakesTheLeaseAndCountsTheAttempt(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        var operation = await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Logs, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();

        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        Assert.NotNull(claimed);
        Assert.Equal(operation.Id, claimed.Id);
        Assert.Equal(PurgeOperationStatus.Processing, claimed.Status);
        Assert.Equal(leaseId, claimed.LeaseId);
        Assert.Equal(Now.AddMinutes(10), claimed.LeaseExpiresAt);
        Assert.Equal(1, claimed.AttemptCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOperationNotYetDue_When_ClaimNextAsync_Then_ReturnsNothing(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Audit, targetId, purgeAfter: Now.AddDays(5));
        var repository = new PurgeOperationRepository(dbContext);

        var claimed = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        Assert.Null(claimed);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnOperationHeldByALiveLease_When_ClaimNextAsync_Then_ReturnsNothing(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Ingestion, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        var second = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        Assert.Null(second);
    }

    [Theory, AutoDomainData]
    public async Task Given_AnExpiredLease_When_ClaimNextAsync_Then_TheOperationIsClaimableAgain(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Search, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        var reclaimed = await repository.ClaimNextAsync(
            Guid.NewGuid(),
            Now.AddMinutes(20),
            Now.AddMinutes(30));

        Assert.NotNull(reclaimed);
        Assert.Equal(2, reclaimed.AttemptCount);
    }

    [Theory, AutoDomainData]
    public async Task Given_ALeaseHolder_When_AdvanceAsync_Then_PersistsTheNewStep(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Search, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        await repository.AdvanceAsync(claimed!.Id, leaseId, "DeleteContent");

        var reloaded = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == claimed.Id);
        Assert.Equal("DeleteContent", reloaded.Step);
    }

    [Theory, AutoDomainData]
    public async Task Given_ANonLeaseHolder_When_AdvanceAsync_Then_DoesNotModifyTheOperation(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Search, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var claimed = await repository.ClaimNextAsync(Guid.NewGuid(), Now, Now.AddMinutes(10));

        await repository.AdvanceAsync(claimed!.Id, Guid.NewGuid(), "ShouldNotApply");

        var reloaded = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == claimed.Id);
        Assert.Equal("Requested", reloaded.Step);
    }

    [Theory, AutoDomainData]
    public async Task Given_ALeaseHolder_When_CompleteAsync_Then_MarksCompletedAndReleasesTheLease(
        Guid databaseId,
        Guid organizationId,
        Guid targetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Audit, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        await repository.CompleteAsync(claimed!.Id, leaseId, Now.AddMinutes(5));

        var reloaded = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == claimed.Id);
        Assert.Equal(PurgeOperationStatus.Completed, reloaded.Status);
        Assert.Equal(Now.AddMinutes(5), reloaded.CompletedAt);
        Assert.Null(reloaded.LeaseId);
        Assert.Null(reloaded.LeaseExpiresAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_ATemporaryFailure_When_FailAsync_Then_SchedulesTheNextAttempt(
        Guid databaseId,
        Guid organizationId,
        Guid targetId,
        string errorCode)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Logs, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        await repository.FailAsync(claimed!.Id, leaseId, errorCode, isPermanent: false, Now.AddMinutes(5));

        var reloaded = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == claimed.Id);
        Assert.Equal(PurgeOperationStatus.TemporaryFailure, reloaded.Status);
        Assert.Equal(errorCode, reloaded.LastErrorCode);
        Assert.Equal(Now.AddMinutes(5), reloaded.NextAttemptAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_APermanentFailure_When_FailAsync_Then_NeverSchedulesAnotherAttempt(
        Guid databaseId,
        Guid organizationId,
        Guid targetId,
        string errorCode)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, organizationId, PurgeOperationScope.Logs, targetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        await repository.FailAsync(claimed!.Id, leaseId, errorCode, isPermanent: true, nextAttemptAt: null);

        var reloaded = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == claimed.Id);
        Assert.Equal(PurgeOperationStatus.PermanentFailure, reloaded.Status);
        Assert.Null(reloaded.NextAttemptAt);
    }

    [Theory, AutoDomainData]
    public async Task Given_TwoOrganizationsWithDueOperations_When_CompletingOne_Then_TheOtherOrganizationsOperationIsUntouched(
        Guid databaseId,
        Guid firstOrganizationId,
        Guid secondOrganizationId,
        Guid firstTargetId,
        Guid secondTargetId)
    {
        await using var dbContext = CreateDbContext(databaseId);
        await SeedAsync(
            dbContext, firstOrganizationId, PurgeOperationScope.Audit, firstTargetId, purgeAfter: Now.AddMinutes(-2));
        var secondOperation = await SeedAsync(
            dbContext, secondOrganizationId, PurgeOperationScope.Audit, secondTargetId, purgeAfter: Now.AddMinutes(-1));
        var repository = new PurgeOperationRepository(dbContext);
        var leaseId = Guid.NewGuid();
        var claimed = await repository.ClaimNextAsync(leaseId, Now, Now.AddMinutes(10));

        await repository.CompleteAsync(claimed!.Id, leaseId, Now);

        var untouched = await dbContext.PurgeOperations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Id == secondOperation.Id);
        Assert.NotEqual(claimed.Id, secondOperation.Id);
        Assert.Equal(PurgeOperationStatus.Pending, untouched.Status);
    }

    private static async Task<PurgeOperation> SeedAsync(
        AssistantCoreDbContext dbContext,
        Guid organizationId,
        PurgeOperationScope scope,
        Guid targetId,
        DateTimeOffset purgeAfter)
    {
        var operation = new PurgeOperation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Scope = scope,
            TargetId = targetId,
            RequestedAt = Now,
            PurgeAfter = purgeAfter,
            Status = PurgeOperationStatus.Pending,
            Step = "Requested"
        };
        dbContext.PurgeOperations.Add(operation);
        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        return operation;
    }

    private static AssistantCoreDbContext CreateDbContext(Guid databaseId)
    {
        var options = new DbContextOptionsBuilder<AssistantCoreDbContext>()
            .UseInMemoryDatabase(databaseId.ToString())
            .ConfigureWarnings(builder => builder.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AssistantCoreDbContext(options);
    }

    private sealed class ThrowOnceDbContext(
        DbContextOptions<AssistantCoreDbContext> options,
        Exception exceptionToThrowOnce) : AssistantCoreDbContext(options)
    {
        private bool _hasThrown;

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (!_hasThrown)
            {
                _hasThrown = true;
                throw exceptionToThrowOnce;
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
