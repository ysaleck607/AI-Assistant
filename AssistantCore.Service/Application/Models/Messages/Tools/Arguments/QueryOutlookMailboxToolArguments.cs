namespace AssistantCore.Service.Application.Models.Messages.Tools.Arguments;

public sealed record QueryOutlookMailboxToolArguments(
    string? Query,
    string? Sender,
    string? Recipient,
    string Scope,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    bool IncludeBody,
    int Limit);
