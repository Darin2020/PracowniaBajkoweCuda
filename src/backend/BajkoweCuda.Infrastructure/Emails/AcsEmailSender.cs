using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>
/// Sends through Azure Communication Services Email with the managed identity (no connection string or key).
/// The identity needs the "Communication and Email Service Owner" role on the Communication Services resource.
/// </summary>
internal sealed class AcsEmailSender(IOptions<EmailOptions> options, TokenCredential credential) : IEmailSender
{
    private readonly Lazy<EmailClient> _client = new(() =>
        new EmailClient(
            options.Value.Acs.Endpoint ?? throw new InvalidOperationException("Email:Acs:Endpoint is not configured."),
            credential));

    public async Task SendAsync(RenderedEmail email, CancellationToken cancellationToken)
    {
        var content = new EmailContent(email.Subject) { Html = email.HtmlBody, PlainText = email.TextBody };
        var message = new EmailMessage(options.Value.From, email.To, content);

        // Started: ACS has accepted the message. Waiting for final delivery would hold the dispatcher for minutes.
        await _client.Value.SendAsync(WaitUntil.Started, message, cancellationToken);
    }
}
