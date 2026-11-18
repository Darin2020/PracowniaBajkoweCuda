namespace BajkoweCuda.Infrastructure.Emails;

public enum EmailProvider
{
    /// <summary>SMTP, for example Mailpit locally.</summary>
    Smtp,

    /// <summary>Azure Communication Services Email, authenticated with the managed identity.</summary>
    Acs,
}

/// <summary>Section <c>Email</c> (docs/PLAN.md §14).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Smtp;

    /// <summary>Sender address. With ACS it must belong to the linked domain.</summary>
    public string From { get; set; } = "no-reply@bajkowecuda.localhost";

    public SmtpOptions Smtp { get; set; } = new();

    public AcsOptions Acs { get; set; } = new();

    /// <summary>A message that fails this many times is given up (status Dead).</summary>
    public int MaxAttempts { get; set; } = 8;

    public sealed class SmtpOptions
    {
        public string Host { get; set; } = "localhost";

        public int Port { get; set; } = 1025;
    }

    public sealed class AcsOptions
    {
        public Uri? Endpoint { get; set; }
    }
}
