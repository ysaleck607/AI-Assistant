using AssistantCore.Repository.Domain.Entities;
using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Repository.Repositories;
using AssistantCore.Service.Application.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.Conversations.Purge;

/// <summary>
/// Execute la purge physique d'une conversation supprimee, etape par etape.
///
/// Chaque etape reussie est persistee avant de passer a la suivante : un arret
/// brutal reprend a la derniere etape confirmee, sans rejouer ce qui est deja
/// fait. Chaque etape est par ailleurs idempotente, si bien qu'une reprise au
/// milieu d'une etape interrompue ne produit ni doublon ni erreur.
///
/// Aucune etape ne vise Azure AI Search : l'index ne contient que du contenu
/// Microsoft 365, jamais de conversation ni de message.
/// </summary>
public sealed class ConversationPurgeService(
    IConversationPurgeRepository purgeRepository,
    IOptions<ConversationPurgeOptions> options,
    TimeProvider timeProvider,
    ILogger<ConversationPurgeService> logger) : IConversationPurgeService
{
    public async Task<bool> PurgeNextAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var leaseId = Guid.NewGuid();

        var request = await purgeRepository.ClaimNextAsync(
            leaseId,
            now,
            now.AddMinutes(settings.LeaseMinutes),
            cancellationToken);

        if (request is null) return false;

        try
        {
            await RunStepsAsync(request, leaseId, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // L'annulation n'est pas un echec : le bail expirera et la demande
            // sera reprise a l'etape confirmee.
            throw;
        }
        catch (Exception exception)
        {
            await RecordFailureAsync(request, leaseId, settings, exception, cancellationToken);
            return true;
        }
    }

    private async Task RunStepsAsync(
        ConversationPurgeRequest request,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        var deletedSources = request.DeletedSourceCount;
        var deletedMessages = request.DeletedMessageCount;

        if (request.Step <= ConversationPurgeStep.DeleteMessageSources)
        {
            deletedSources = await purgeRepository.DeleteMessageSourcesAsync(
                request.Id,
                leaseId,
                cancellationToken);
            await purgeRepository.AdvanceAsync(
                request.Id,
                leaseId,
                ConversationPurgeStep.DeleteMessages,
                cancellationToken);
        }

        if (request.Step <= ConversationPurgeStep.DeleteMessages)
        {
            deletedMessages = await purgeRepository.DeleteMessagesAsync(
                request.Id,
                leaseId,
                cancellationToken);
            await purgeRepository.AdvanceAsync(
                request.Id,
                leaseId,
                ConversationPurgeStep.DeleteConversation,
                cancellationToken);
        }

        if (request.Step <= ConversationPurgeStep.DeleteConversation)
        {
            await purgeRepository.DeleteConversationAsync(request.Id, leaseId, cancellationToken);
            await purgeRepository.AdvanceAsync(
                request.Id,
                leaseId,
                ConversationPurgeStep.Verify,
                cancellationToken);
        }

        // Completed n'est enregistre qu'apres avoir constate qu'il ne reste rien.
        // Marquer la purge terminee sans verifier laisserait croire qu'une donnee
        // a disparu alors qu'elle subsiste.
        if (!await purgeRepository.VerifyPurgedAsync(request.Id, cancellationToken))
        {
            throw new InvalidOperationException("Conversation purge verification failed.");
        }

        await purgeRepository.CompleteAsync(
            request.Id,
            leaseId,
            timeProvider.GetUtcNow(),
            cancellationToken);

        // La trace ne porte que des nombres : jamais un titre, un message ou une
        // source, qui sont precisement ce que la purge vient d'effacer.
        logger.LogInformation(
            "Conversation purge completed for organization {OrganizationId}: {MessageCount} messages, {SourceCount} sources, {AttemptCount} attempts.",
            request.OrganizationId,
            deletedMessages,
            deletedSources,
            request.AttemptCount);
    }

    private async Task RecordFailureAsync(
        ConversationPurgeRequest request,
        Guid leaseId,
        ConversationPurgeOptions settings,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isPermanent = request.AttemptCount >= settings.MaximumAttempts;
        // Le delai double a chaque tentative : une base indisponible n'est pas
        // harcelee, et la reprise reste automatique quand elle revient.
        var backoff = TimeSpan.FromMinutes(
            settings.RetryBackoffMinutes * Math.Pow(2, Math.Max(0, request.AttemptCount - 1)));

        // Le message d'exception peut porter du contenu : seul son type est trace.
        logger.LogWarning(
            "Conversation purge failed at step {Step} for organization {OrganizationId} ({FailureType}); attempt {AttemptCount}; permanent: {IsPermanent}.",
            request.Step,
            request.OrganizationId,
            exception.GetType().Name,
            request.AttemptCount,
            isPermanent);

        await purgeRepository.FailAsync(
            request.Id,
            leaseId,
            exception.GetType().Name,
            isPermanent,
            isPermanent ? null : timeProvider.GetUtcNow().Add(backoff),
            CancellationToken.None);
    }
}
