namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>A rendered email, ready to send. <paramref name="Images"/> are shown in the HTML as <c>cid:</c> links.</summary>
public sealed record RenderedEmail(string To, string Subject, string HtmlBody, string TextBody, IReadOnlyList<InlineImage>? Images = null);

/// <summary>An image sent inside the email (a chat photo thumbnail), referenced from the HTML as <c>cid:{ContentId}</c>.</summary>
public sealed record InlineImage(string ContentId, string ContentType, byte[] Content);

/// <summary>Delivers a rendered email: <see cref="SmtpEmailSender"/> or <see cref="AcsEmailSender"/> (docs/PLAN.md §3.6).</summary>
public interface IEmailSender
{
    Task SendAsync(RenderedEmail email, CancellationToken cancellationToken);
}
