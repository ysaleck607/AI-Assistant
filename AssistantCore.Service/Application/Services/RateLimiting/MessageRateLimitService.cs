using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using AssistantCore.Service.Application.Services.Incidents;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.RateLimiting;

public sealed class MessageRateLimitService(
    IRateLimitStore store,
    IOptions<RateLimitingOptions> options,
    IOrganizationCapacityAlertGate capacityAlertGate,
    IOperationalIncidentReporter incidentReporter) : IMessageRateLimitService
{
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private readonly RateLimitingOptions _options = options.Value;

    public async Task EnsureAllowedAsync(
        MessageUserContext userContext,
        CancellationToken cancellationToken)
    {
        var result = await store.TryAcquireAsync(
            [
                new RateLimitRule(
                    CreateMemberKey(userContext.Organization.Id, userContext.Member.Id),
                    _options.MemberMessagesPerMinute,
                    Window),
                new RateLimitRule(
                    CreateOrganizationKey(userContext.Organization.Id),
                    _options.OrganizationMessagesPerMinute,
                    Window)
            ],
            cancellationToken);

        if (result.IsAllowed)
        {
            return;
        }

        var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));

        // Feature #1 : faute d'un vrai concept de quota persistant, le debit rejete est le
        // meilleur proxy de capacite disponible ce soir. Un incident par organisation au
        // plus toutes les 5 minutes (voir IOrganizationCapacityAlertGate) pour ne pas noyer
        // le digest email si l'organisation reste bloquee sur sa limite en continu.
        if (capacityAlertGate.TryAcquire(userContext.Organization.Id))
        {
            await incidentReporter.ReportAsync(
                new OperationalIncidentReport(
                    new RequestRateLimitExceededException(retryAfterSeconds),
                    $"rate-limit-{Guid.NewGuid():N}",
                    userContext.Organization.Id,
                    userContext.Member.Id),
                cancellationToken);
        }

        throw new RequestRateLimitExceededException(retryAfterSeconds);
    }

    private static string CreateOrganizationKey(Guid organizationId) =>
        $"rate:organization:{organizationId:N}:messages";

    private static string CreateMemberKey(Guid organizationId, Guid memberId) =>
        $"rate:member:{organizationId:N}:{memberId:N}:messages";
}
