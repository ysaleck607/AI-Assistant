using AssistantCore.Service.Application.Configuration;
using AssistantCore.Service.Application.Exceptions;
using AssistantCore.Service.Application.Models.Messages;
using Microsoft.Extensions.Options;

namespace AssistantCore.Service.Application.Services.RateLimiting;

public sealed class MessageRateLimitService(
    IRateLimitStore store,
    IOptions<RateLimitingOptions> options) : IMessageRateLimitService
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
        throw new RequestRateLimitExceededException(retryAfterSeconds);
    }

    private static string CreateOrganizationKey(Guid organizationId) =>
        $"rate:organization:{organizationId:N}:messages";

    private static string CreateMemberKey(Guid organizationId, Guid memberId) =>
        $"rate:member:{organizationId:N}:{memberId:N}:messages";
}
