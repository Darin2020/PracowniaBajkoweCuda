namespace BajkoweCuda.Infrastructure.Emails;

public enum EmailKind
{
    Transactional,
    ChatDigest,
}

public enum EmailStatus
{
    Pending,
    Sent,
    Skipped,
    Failed,
    Dead,
}

/// <summary>A row of the email outbox (docs/PLAN.md §5, EmailOutbox). The dispatcher renders and sends it.</summary>
public sealed class EmailOutboxMessage
{
    public const int AddressMaxLength = 256;
    public const int TemplateMaxLength = 64;
    public const int LastErrorMaxLength = 1000;

    public Guid Id { get; set; }

    public EmailKind Kind { get; set; }

    public Guid? ToUserId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Template { get; set; } = string.Empty;

    /// <summary>Template values (JSON object of strings), for example the confirmation link.</summary>
    public string PayloadJson { get; set; } = "{}";

    public Guid? ConversationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Not sent before this time: delayed emails and retries.</summary>
    public DateTimeOffset SendAfter { get; set; }

    public EmailStatus Status { get; set; }

    public int Attempts { get; set; }

    /// <summary>Type and message of the last failure, with email addresses masked.</summary>
    public string? LastError { get; set; }

    public DateTimeOffset? SentAt { get; set; }
}
