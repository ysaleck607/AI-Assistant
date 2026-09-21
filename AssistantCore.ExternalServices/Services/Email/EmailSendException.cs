namespace AssistantCore.ExternalServices.Services.Email;

public sealed class EmailSendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
