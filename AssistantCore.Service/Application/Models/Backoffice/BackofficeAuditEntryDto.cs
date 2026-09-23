using System.Text.Json;
using AssistantCore.Repository.Queries;

namespace AssistantCore.Service.Application.Models.Backoffice;

public sealed record BackofficeAuditEntryDto(
    Guid Id,
    DateTimeOffset Timestamp,
    string Action,
    Guid ActorUserId,
    string? ActorEmail,
    Guid OrganizationId,
    string OrganizationName,
    string TargetType,
    Guid TargetId,
    string Result,
    string CorrelationId,
    IReadOnlyCollection<BackofficeAuditChangeDto> Changes)
{
    public static BackofficeAuditEntryDto FromData(BackofficeAuditEntryData data) => new(
        data.Id,
        data.OccurredAt,
        data.Action.ToString(),
        data.ActorId,
        // Un acteur administratif (equipe Synaptix) n'est pas un OrganizationMember :
        // aucune jointure ne permet de resoudre son email depuis ce seul enregistrement.
        ActorEmail: null,
        data.OrganizationId,
        data.OrganizationName,
        data.TargetType,
        data.TargetId,
        // Aucune ecriture d'echec n'existe aujourd'hui (voir AdministrativeAuditEntryFactory) :
        // toute entree persistee represente une action reussie.
        Result: "Succeeded",
        data.CorrelationId,
        BuildChanges(data.OldValuesJson, data.NewValuesJson));

    private static IReadOnlyCollection<BackofficeAuditChangeDto> BuildChanges(
        string oldValuesJson,
        string newValuesJson)
    {
        var oldValues = ParseValues(oldValuesJson);
        var newValues = ParseValues(newValuesJson);

        return oldValues.Keys
            .Union(newValues.Keys, StringComparer.Ordinal)
            .OrderBy(field => field, StringComparer.Ordinal)
            .Select(field => new BackofficeAuditChangeDto(
                field,
                oldValues.GetValueOrDefault(field),
                newValues.GetValueOrDefault(field)))
            .ToArray();
    }

    private static Dictionary<string, string?> ParseValues(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.ToString(),
                StringComparer.Ordinal);
    }
}
