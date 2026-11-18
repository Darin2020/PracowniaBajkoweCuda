namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>A rendered email, ready to send.</summary>
public sealed record RenderedEmail(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>Delivers a rendered email: <see cref="SmtpEmailSender"/> or <see cref="AcsEmailSender"/> (docs/PLAN.md §3.6).</summary>
public interface IEmailSender
{
    Task SendAsync(RenderedEmail email, CancellationToken cancellationToken);
}
