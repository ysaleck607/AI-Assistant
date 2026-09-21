namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeAuditChangeDto(
    string Field,
    string? Before,
    string? After);
