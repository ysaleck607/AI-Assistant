using System.Data;
using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class PurgeOperationRepository(AssistantCoreDbContext dbContext)
    : IPurgeOperationRepository
{
    public async Task<PurgeOperation> RequestAsync(
        Guid organizationId,
        PurgeOperationScope scope,
        Guid targetId,
        DateTimeOffset requestedAt,
        DateTimeOffset purgeAfter,
        string initialStep,
        CancellationToken cancellationToken = default)
    {
        var operation = new PurgeOperation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Scope = scope,
            TargetId = targetId,
            RequestedAt = requestedAt,
            PurgeAfter = purgeAfter,
            Status = PurgeOperationStatus.Pending,
            Step = initialStep
        };
        dbContext.PurgeOperations.Add(operation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return operation;
        }
        catch (DbUpdateException exception) when (IsIdentityConflict(exception))
        {
            dbContext.Entry(operation).State = EntityState.Detached;

            var competingOperation = await dbContext.PurgeOperations
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    candidate =>
                        candidate.OrganizationId == organizationId
                        && candidate.Scope == scope
                        && candidate.TargetId == targetId,
                    cancellationToken);

            return competingOperation
                ?? throw new InvalidOperationException(
                    "The purge operation created by the competing request could not be reloaded.");
        }
    }

    public async Task<PurgeOperation?> ClaimNextAsync(
        Guid leaseId,
        DateTimeOffset now,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        // Serializable : deux workers qui reclament en meme temps ne doivent pas
        // obtenir la meme operation, sans quoi la purge serait executee deux fois.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        var operation = await dbContext.PurgeOperations
            .Where(candidate =>
                candidate.PurgeAfter <= now
                && (candidate.Status == PurgeOperationStatus.Pending
                    || candidate.Status == PurgeOperationStatus.TemporaryFailure
                        && (candidate.NextAttemptAt == null || candidate.NextAttemptAt <= now)
                    // Un bail expire signale un worker disparu en cours de route.
                    || candidate.Status == PurgeOperationStatus.Processing
                        && candidate.LeaseExpiresAt <= now))
            .OrderBy(candidate => candidate.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (operation is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        operation.Status = PurgeOperationStatus.Processing;
        operation.LeaseId = leaseId;
        operation.LeaseExpiresAt = leaseExpiresAt;
        operation.AttemptCount++;
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return operation;
    }

    public async Task AdvanceAsync(
        Guid operationId,
        Guid leaseId,
        string nextStep,
        CancellationToken cancellationToken = default)
    {
        var operation = await FindLeasedAsync(operationId, leaseId, cancellationToken);
        if (operation is null) return;

        operation.Step = nextStep;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync(
        Guid operationId,
        Guid leaseId,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken = default)
    {
        var operation = await FindLeasedAsync(operationId, leaseId, cancellationToken);
        if (operation is null) return;

        operation.Status = PurgeOperationStatus.Completed;
        operation.CompletedAt = completedAt;
        operation.LeaseId = null;
        operation.LeaseExpiresAt = null;
        operation.LastErrorCode = null;
        operation.NextAttemptAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(
        Guid operationId,
        Guid leaseId,
        string errorCode,
        bool isPermanent,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        var operation = await FindLeasedAsync(operationId, leaseId, cancellationToken);
        if (operation is null) return;

        operation.Status = isPermanent
            ? PurgeOperationStatus.PermanentFailure
            : PurgeOperationStatus.TemporaryFailure;
        operation.LastErrorCode = errorCode;
        operation.NextAttemptAt = isPermanent ? null : nextAttemptAt;
        operation.LeaseId = null;
        operation.LeaseExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Une ecriture n'est permise qu'au detenteur du bail. Un worker dont le bail
    /// a expire, et dont l'operation a ete reprise ailleurs, ne doit plus rien
    /// modifier : il obtient null et s'arrete sans consequence.
    /// </summary>
    private Task<PurgeOperation?> FindLeasedAsync(
        Guid operationId,
        Guid leaseId,
        CancellationToken cancellationToken) =>
        dbContext.PurgeOperations
            .FirstOrDefaultAsync(
                candidate => candidate.Id == operationId && candidate.LeaseId == leaseId,
                cancellationToken);

    private static bool IsIdentityConflict(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;

        return message.Contains(
                   "IX_PurgeOperation_OrganizationId_Scope_TargetId",
                   StringComparison.OrdinalIgnoreCase)
               || (message.Contains(nameof(PurgeOperation.OrganizationId), StringComparison.OrdinalIgnoreCase)
                   && message.Contains(nameof(PurgeOperation.Scope), StringComparison.OrdinalIgnoreCase)
                   && message.Contains(nameof(PurgeOperation.TargetId), StringComparison.OrdinalIgnoreCase));
    }
}
