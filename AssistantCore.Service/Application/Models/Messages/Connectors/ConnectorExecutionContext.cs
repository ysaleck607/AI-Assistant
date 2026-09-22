using AssistantCore.Repository.Domain.Enums;
using AssistantCore.Service.Application.Models.Messages.AiModels;

namespace AssistantCore.Service.Application.Models.Messages.Connectors;

public sealed record ConnectorExecutionContext(
    Guid OrganizationId,
    Guid MemberId,
    string? ExternalTenantId = null,
    Guid? EntraUserId = null,
    IdentityProvider? IdentityProvider = null,
    int RetrievalCandidateLimit = int.MaxValue,
    string? UserEmail = null,
    IReadOnlyCollection<AiConversationMessage>? ConversationHistory = null,
    string? CurrentUserMessage = null);
