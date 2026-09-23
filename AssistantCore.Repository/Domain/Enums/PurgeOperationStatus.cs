namespace AssistantCore.Repository.Domain.Enums;

public enum PurgeOperationStatus
{
    Pending = 1,
    Completed = 2,
    Processing = 3,
    TemporaryFailure = 4,
    PermanentFailure = 5
}
