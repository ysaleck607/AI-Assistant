namespace AssistantCore.Repository.Domain.Enums;

public enum AdministrativeAuditAction
{
    MemberRoleChanged,
    MemberStatusChanged,
    UsagePolicyChanged,
    ConversationArchived,
    ConversationDeleted,
    ConnectorStatusChanged,
    UserAccessReevaluated,
    Microsoft365ReindexRequested
}
