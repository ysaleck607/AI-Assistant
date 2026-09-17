namespace AssistantCore.Repository.Domain.Enums;

public enum Microsoft365ReindexOperationStatus
{
    Pending,
    Running,
    Succeeded,
    TemporaryFailure,
    PermanentFailure
}
