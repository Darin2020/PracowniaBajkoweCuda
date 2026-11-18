using System.Text.RegularExpressions;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>Email addresses never appear in logs or stored error texts in full (CLAUDE.md, Rules).</summary>
public static partial class EmailAddresses
{
    /// <summary><c>anna.nowak@example.com</c> → <c>a***@example.com</c>.</summary>
    public static string Mask(string address)
    {
        var at = address.LastIndexOf('@');
        return at <= 0 ? "***" : $"{address[0]}***{address[at..]}";
    }

    /// <summary>Masks every address inside a free text, such as an exception message from a mail server.</summary>
    public static string MaskAll(string text) => Address().Replace(text, match => Mask(match.Value));

    [GeneratedRegex(@"[^\s<>""'@,;:()\[\]]+@[^\s<>""'@,;:()\[\]]+")]
    private static partial Regex Address();
}
