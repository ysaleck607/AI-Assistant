using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace AssistantCore.ExternalServices.Services.Email;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var configuration = options.Value;

        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(configuration.SenderDisplayName, configuration.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.ToAddress));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new TextPart("plain") { Text = message.PlainTextBody };

        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(
                configuration.Host,
                configuration.Port,
                SecureSocketOptions.StartTls,
                cancellationToken);
            await client.AuthenticateAsync(configuration.Username, configuration.Password, cancellationToken);
            await client.SendAsync(mimeMessage, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new EmailSendException(
                $"Failed to send email to {message.ToAddress} via {configuration.Host}:{configuration.Port}.",
                exception);
        }
        finally
        {
            if (client.IsConnected)
            {
                await client.DisconnectAsync(true, cancellationToken);
            }
        }
    }
}
