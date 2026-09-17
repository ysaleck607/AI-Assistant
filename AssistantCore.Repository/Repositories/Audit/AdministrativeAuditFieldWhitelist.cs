using AssistantCore.Repository.Domain.Enums;

namespace AssistantCore.Repository.Repositories.Audit;

public static class AdministrativeAuditFieldWhitelist
{
    private static readonly IReadOnlyDictionary<AdministrativeAuditAction, IReadOnlySet<string>> AllowedFields =
        new Dictionary<AdministrativeAuditAction, IReadOnlySet<string>>
        {
            [AdministrativeAuditAction.MemberRoleChanged] = new HashSet<string> { "role" },
            [AdministrativeAuditAction.MemberStatusChanged] = new HashSet<string> { "status" },
            [AdministrativeAuditAction.UsagePolicyChanged] = new HashSet<string>
            {
                "monthlyTokenLimit", "status", "effectiveAt", "version"
            },
            [AdministrativeAuditAction.ConversationArchived] = new HashSet<string> { "status" },
            [AdministrativeAuditAction.ConversationDeleted] = new HashSet<string> { "deletedAt" },
            [AdministrativeAuditAction.ConnectorStatusChanged] = new HashSet<string> { "status", "isIndexed" },
            [AdministrativeAuditAction.UserAccessReevaluated] = new HashSet<string>
            {
                "accessAllowed", "diagnosticCode"
            },
            [AdministrativeAuditAction.Microsoft365ReindexRequested] = new HashSet<string>
            {
                "operationId", "libraryCount", "reason"
            }
        };

    public static void Validate(AdministrativeAuditAction action, IReadOnlyDictionary<string, object?> values)
    {
        var allowedFields = AllowedFields[action];
        var invalidField = values.Keys.FirstOrDefault(key => !allowedFields.Contains(key));

        if (invalidField is not null)
        {
            throw new InvalidOperationException(
                $"Field '{invalidField}' is not allowed in audit values for action '{action}'.");
        }
    }
}
