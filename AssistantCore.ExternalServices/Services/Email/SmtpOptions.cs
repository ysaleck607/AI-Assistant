namespace AssistantCore.ExternalServices.Services.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    public string Username { get; init; } = string.Empty;

    public string Password { get; init; } = string.Empty;

    public string SenderDisplayName { get; init; } = string.Empty;

    public string SenderAddress { get; init; } = string.Empty;
}
