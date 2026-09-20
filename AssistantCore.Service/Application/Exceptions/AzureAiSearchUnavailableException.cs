namespace AssistantCore.Service.Application.Exceptions;

public sealed class AzureAiSearchUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
