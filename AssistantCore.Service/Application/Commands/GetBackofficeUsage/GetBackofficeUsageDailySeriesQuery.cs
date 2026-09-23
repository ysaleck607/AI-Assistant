using AssistantCore.Service.Application.Abstractions;
using AssistantCore.Service.Application.Models.Backoffice;

namespace AssistantCore.Service.Application.Commands.GetBackofficeUsage;

public sealed record GetBackofficeUsageDailySeriesQuery(int Days) : IRequest<BackofficeUsageDailySeriesResponse>;
