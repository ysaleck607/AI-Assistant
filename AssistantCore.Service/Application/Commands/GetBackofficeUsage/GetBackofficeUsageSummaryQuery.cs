using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed record GetBackofficeUsageSummaryQuery : IRequest<BackofficeUsageSummaryDto>;
