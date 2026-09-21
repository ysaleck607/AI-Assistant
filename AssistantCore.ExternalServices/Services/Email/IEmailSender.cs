namespace AssistantCore.ExternalServices.Services.Email;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
