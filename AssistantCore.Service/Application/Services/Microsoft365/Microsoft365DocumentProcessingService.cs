using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Microsoft365;
using AssistantCore.Service.Application.Models.Microsoft365.ContentExtraction;
using AssistantCore.Service.Application.Models.Microsoft365.Permissions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Microsoft365;

public sealed class Microsoft365DocumentProcessingService(
    IMicrosoft365DocumentWorkProcessingRepository workRepository,
    IMicrosoft365IndexedContentRepository indexedContentRepository,
    IMicrosoft365DriveContentClient contentClient,
    IMicrosoft365ContentExtractionService extractionService,
    IMicrosoft365DocumentChunkingService chunkingService,
    IMicrosoft365EmbeddingGenerator embeddingGenerator,
    IMicrosoft365AclResolver aclResolver,
    IMicrosoft365PassageIndexWriter indexWriter,
    IMicrosoft365ContentAclSynchronizationService aclSynchronizationService,
    IOptions<Microsoft365Options> options,
    TimeProvider timeProvider,
    ILogger<Microsoft365DocumentProcessingService> logger) : IMicrosoft365DocumentProcessingService
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();
        var work = await workRepository.ClaimNextAsync(
            leaseId,
            now,
            now.AddMinutes(options.Value.DocumentWorkLeaseMinutes),
            cancellationToken);
        if (work is null)
        {
            return false;
        }

        try
        {
            if (work.WorkType == Microsoft365DocumentWorkType.DeleteDocument)
            {
                await DeleteAsync(work, cancellationToken);
            }
            else
            {
                await ProcessAsync(work, cancellationToken);
            }

            await workRepository.CompleteAsync(work, timeProvider.GetUtcNow(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Microsoft 365 document work {WorkId} failed for drive item {DriveItemId}.",
                work.Id,
                work.DriveItemId);
            var failure = Microsoft365DocumentFailurePolicy.Evaluate(
                exception,
                work.AttemptCount,
                options.Value);
            await workRepository.FailAsync(
                work,
                failure.IsPermanent,
                GetErrorCode(exception),
                timeProvider.GetUtcNow().Add(failure.RetryDelay),
                cancellationToken);
        }

        return true;
    }

    private async Task ProcessAsync(
        Microsoft365DocumentWork work,
        CancellationToken cancellationToken)
    {
        var source = work.Microsoft365Source;
        var connection = source.Microsoft365Connection;
        if (source.Status != Microsoft365SourceStatus.Enabled
            || !source.IsIndexed
            || connection.Status != Microsoft365ConnectionStatus.Active
            || string.IsNullOrWhiteSpace(connection.TenantId)
            || string.IsNullOrWhiteSpace(work.Name)
            || string.IsNullOrWhiteSpace(work.ETag))
        {
            throw new InvalidDataException("The Microsoft 365 document work is no longer indexable.");
        }

        var existing = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            source.Id,
            work.DriveItemId,
            cancellationToken);
        var documentIndexVersion = Microsoft365DocumentIndexVersion.Create(work.ETag);
        if (existing is { IsAvailable: true }
            && string.Equals(
                existing.DocumentVersion,
                documentIndexVersion,
                StringComparison.Ordinal))
        {
            return;
        }

        var reference = new Microsoft365ContentReference(
            Microsoft365ContentReferenceKind.DriveItem,
            work.SiteId,
            work.DriveId,
            ListId: null,
            work.DriveItemId);
        var resolution = await aclResolver.ResolveAsync(
            work.Organization,
            reference,
            cancellationToken);
        if (resolution is not Microsoft365AclResolution.ResolvedAcl resolved)
        {
            await aclSynchronizationService.MarkUnavailableIfRegisteredAsync(
                work.OrganizationId,
                source.Id,
                work.DriveItemId,
                cancellationToken);
            throw new Microsoft365AclResolutionException();
        }

        var bytes = await contentClient.DownloadAsync(
            connection.TenantId,
            work.DriveId,
            work.DriveItemId,
            cancellationToken);
        await using var stream = new MemoryStream(bytes, writable: false);
        var extraction = await extractionService.ExtractAsync(
            new Microsoft365ContentExtractionRequest(work.Name, work.MimeType, stream, bytes.Length),
            cancellationToken);
        if (extraction.Status != Microsoft365ContentExtractionStatus.Success)
        {
            throw new Microsoft365ContentExtractionException(
                extraction.Status,
                work.Name,
                work.MimeType);
        }

        var sourceType = source.Kind == Microsoft365SourceKind.OneDrive
            ? "onedrive"
            : "sharepoint";
        var passages = chunkingService.CreateChunks(
            work.OrganizationId,
            source.Id,
            work.SiteId,
            work.DriveId,
            work.DriveItemId,
            documentIndexVersion,
            Path.GetFileNameWithoutExtension(work.Name),
            work.WebUrl,
            work.LastModifiedDateTime,
            extraction.Units)
            .Select(passage => passage with { SourceType = sourceType })
            .ToArray();
        if (passages.Length == 0)
        {
            throw new InvalidDataException("Document extraction did not produce indexable passages.");
        }

        var vectors = await embeddingGenerator.CreateAsync(
            passages.Select(BuildEmbeddingContent).ToArray(),
            cancellationToken);
        var embeddedPassages = passages
            .Select((passage, index) => passage with { ContentVector = vectors[index] })
            .ToArray();
        var obsoleteChunkIds = existing?.Passages.Select(passage => passage.ChunkId).ToHashSet()
            ?? [];

        await indexWriter.MergeOrUploadAsync(
            work.OrganizationId,
            embeddedPassages,
            resolved.Acl,
            cancellationToken);
        var currentChunkIds = embeddedPassages.Select(passage => passage.ChunkId).ToHashSet();
        var obsolete = obsoleteChunkIds.Where(chunkId => !currentChunkIds.Contains(chunkId)).ToArray();
        if (obsolete.Length > 0)
        {
            await indexWriter.DeleteAsync(obsolete, cancellationToken);
        }

        await aclSynchronizationService.RegisterAsync(
            work.OrganizationId,
            source.Id,
            work.DriveItemId,
            embeddedPassages.Select(passage => passage.ChunkId).ToArray(),
            resolved.Acl.Fingerprint,
            source.WebUrl,
            cancellationToken);

        await aclSynchronizationService.SynchronizeAsync(
            work.OrganizationId,
            source.Id,
            work.DriveItemId,
            resolved.Acl,
            cancellationToken);
        var indexed = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            source.Id,
            work.DriveItemId,
            cancellationToken) ?? throw new InvalidOperationException("Indexed content was not registered.");
        indexed.DocumentVersion = documentIndexVersion;
        indexed.Title = Path.GetFileNameWithoutExtension(work.Name);
        indexed.WebUrl = work.WebUrl;
        indexed.LastModifiedAt = work.LastModifiedDateTime;
        await indexedContentRepository.SaveAsync(indexed, cancellationToken);
    }

    private static string BuildEmbeddingContent(Microsoft365SearchPassage passage) =>
        $"Document: {passage.Title}\n\n{passage.Content}";

    private static string GetErrorCode(Exception exception) =>
        exception is Microsoft365ContentExtractionException extractionException
            ? extractionException.ErrorCode
            : exception.GetType().Name;

    private async Task DeleteAsync(
        Microsoft365DocumentWork work,
        CancellationToken cancellationToken)
    {
        var content = await indexedContentRepository.FindAsync(
            work.OrganizationId,
            work.Microsoft365SourceId,
            work.DriveItemId,
            cancellationToken);
        if (content is null)
        {
            return;
        }

        var chunkIds = content.Passages.Select(passage => passage.ChunkId).ToArray();
        if (chunkIds.Length > 0)
        {
            await indexWriter.DeleteAsync(chunkIds, cancellationToken);
        }

        await indexedContentRepository.DeleteAsync(content, cancellationToken);
    }
}
