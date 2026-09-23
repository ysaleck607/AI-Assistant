using AssistantCore.Service.Application.Models.Messages;

namespace AssistantCore.Service.Application.Services.RateLimiting;

public interface IMessageRateLimitService
{
    Task EnsureAllowedAsync(
        MessageUserContext userContext,
        CancellationToken cancellationToken);
}
