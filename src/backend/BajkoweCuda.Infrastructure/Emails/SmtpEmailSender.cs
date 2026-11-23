using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>Sends through SMTP. Used locally with Mailpit (no authentication, no TLS).</summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(settings.From));
        message.To.Add(MailboxAddress.Parse(email.To));
        message.Subject = email.Subject;
        var body = new BodyBuilder { HtmlBody = email.HtmlBody, TextBody = email.TextBody };
        foreach (var image in email.Images ?? [])
        {
            var resource = body.LinkedResources.Add($"{image.ContentId}.jpg", image.Content, ContentType.Parse(image.ContentType));
            resource.ContentId = image.ContentId;
        }

        message.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Smtp.Host, settings.Smtp.Port, SecureSocketOptions.StartTlsWhenAvailable, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
