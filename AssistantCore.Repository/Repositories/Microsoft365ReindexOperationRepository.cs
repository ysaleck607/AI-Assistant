using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AssistantCore.Repository.Repositories;

public sealed class Microsoft365ReindexOperationRepository(AssistantCoreDbContext dbContext)
    : IMicrosoft365ReindexOperationRepository
{
    private const int UniqueConstraintViolation = 2627;
    private const int DuplicateIndexKeyViolation = 2601;

    public async Task<Microsoft365ReindexOperation?> CreateAsync(
        Microsoft365ReindexOperation operation,
        IReadOnlyCollection<Guid> sourceIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(sourceIds);

        foreach (var sourceId in sourceIds)
        {
            operation.Synchronizations.Add(new Microsoft365Synchronization
            {
                Id = Guid.NewGuid(),
                Microsoft365SourceId = sourceId,
                Microsoft365ReindexOperationId = operation.Id,
                Type = Microsoft365SynchronizationType.Initial,
                Status = Microsoft365SynchronizationStatus.Pending,
                RequestedAt = operation.RequestedAt
            });
        }

        dbContext.Microsoft365ReindexOperations.Add(operation);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return operation;
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.Entry(operation).State = EntityState.Detached;
            return null;
        }
    }

    public Task<Microsoft365ReindexOperation?> FindActiveByOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365ReindexOperations
            .AsNoTracking()
            .Where(operation =>
                operation.OrganizationId == organizationId
                && (operation.Status == Microsoft365ReindexOperationStatus.Pending
                    || operation.Status == Microsoft365ReindexOperationStatus.Running))
            .OrderByDescending(operation => operation.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<Microsoft365ReindexOperation?> FindAsync(
        Guid organizationId,
        Guid operationId,
        CancellationToken cancellationToken = default) =>
        dbContext.Microsoft365ReindexOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(operation =>
                operation.Id == operationId
                && operation.OrganizationId == organizationId,
                cancellationToken);

    public async Task<IReadOnlyCollection<Microsoft365ReindexOperation>> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Microsoft365ReindexOperations
            .Where(operation =>
                operation.Status == Microsoft365ReindexOperationStatus.Pending
                || operation.Status == Microsoft365ReindexOperationStatus.Running)
            .OrderBy(operation => operation.RequestedAt)
            .ToArrayAsync(cancellationToken);

    public async Task<Microsoft365ReindexProgressData> GetProgressAsync(
        Guid operationId,
        int maximumDocumentAttempts,
        CancellationToken cancellationToken = default)
    {
        var synchronizations = await dbContext.Microsoft365Synchronizations
            .AsNoTracking()
            .Where(synchronization => synchronization.Microsoft365ReindexOperationId == operationId)
            .Select(synchronization => new Microsoft365ReindexSynchronizationState(
                synchronization.Id,
                synchronization.Microsoft365SourceId,
                synchronization.Status,
                synchronization.IgnoredCount,
                synchronization.StartedAt,
                synchronization.CompletedAt,
                synchronization.LastErrorCode))
            .ToArrayAsync(cancellationToken);

        var works = dbContext.Microsoft365DocumentWorks
            .AsNoTracking()
            .Where(work =>
                work.Microsoft365Synchronization.Microsoft365ReindexOperationId == operationId
                && work.WorkType == Microsoft365DocumentWorkType.ProcessDocument);

        var documents = new Microsoft365ReindexDocumentCounts(
            await works.CountAsync(cancellationToken),
            await works.CountAsync(
                work => work.Status == Microsoft365DocumentWorkStatus.Completed,
                cancellationToken),
            await works.CountAsync(
                work => work.Status == Microsoft365DocumentWorkStatus.PermanentFailure
                    || (work.Status == Microsoft365DocumentWorkStatus.TemporaryFailure
                        && work.AttemptCount >= maximumDocumentAttempts),
                cancellationToken),
            await works.CountAsync(
                work => work.Status == Microsoft365DocumentWorkStatus.Pending
                    || work.Status == Microsoft365DocumentWorkStatus.Processing
                    || (work.Status == Microsoft365DocumentWorkStatus.TemporaryFailure
                        && work.AttemptCount < maximumDocumentAttempts),
                cancellationToken));

        return new Microsoft365ReindexProgressData(synchronizations, documents);
    }

    public async Task<IReadOnlyCollection<string>> GetVisitedDocumentIdsAsync(
        Guid operationId,
        Guid sourceId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Microsoft365DocumentWorks
            .AsNoTracking()
            .Where(work =>
                work.Microsoft365SourceId == sourceId
                && work.Microsoft365Synchronization.Microsoft365ReindexOperationId == operationId
                && work.WorkType == Microsoft365DocumentWorkType.ProcessDocument)
            .Select(work => work.DriveItemId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

    public Task SaveAsync(
        Microsoft365ReindexOperation operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var entry = dbContext.Entry(operation);
        if (entry.State == EntityState.Detached)
        {
            dbContext.Microsoft365ReindexOperations.Attach(operation);
            entry.State = EntityState.Modified;
        }

        return dbContext.SaveChangesAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException
        {
            Number: UniqueConstraintViolation or DuplicateIndexKeyViolation
        };
}
