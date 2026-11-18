namespace BajkoweCuda.Application.Emails;

/// <summary>
/// Queues an email in the database (transactional outbox). A background dispatcher sends it, with retries,
/// so a slow or failing mail service never fails the request that caused the email.
/// </summary>
public interface IEmailOutbox
{
    /// <summary>
    /// Adds the email to the outbox and saves it. <paramref name="payload"/> holds template values such as links;
    /// it must not contain secrets beyond the one-time token in a link.
    /// </summary>
    Task EnqueueAsync(
        EmailTemplate emailTemplate,
        Guid? toUserId,
        string toEmail,
        string language,
        IReadOnlyDictionary<string, string> payload,
        CancellationToken cancellationToken);
}
