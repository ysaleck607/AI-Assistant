using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed record GetBackofficeUsageByOrganizationQuery(
    DateTimeOffset? From,
    DateTimeOffset? To) : IRequest<BackofficeUsageByOrganizationResponse>;
