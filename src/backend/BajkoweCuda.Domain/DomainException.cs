namespace BajkoweCuda.Domain;

/// <summary>
/// A domain rule was broken (an empty message, too many attachments, ...). <see cref="Code"/> is the stable error code
/// the API returns in its problem details and the frontend translates (docs/PLAN.md §4.2).
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException()
        : this("domain.rule_broken", "A domain rule was broken.")
    {
    }

    public DomainException(string message)
        : this("domain.rule_broken", message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "domain.rule_broken";
    }

    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public string Code { get; }
}
