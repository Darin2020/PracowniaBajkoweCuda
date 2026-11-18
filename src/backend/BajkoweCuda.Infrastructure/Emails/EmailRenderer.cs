using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Encodings.Web;
using BajkoweCuda.Application.Emails;
using BajkoweCuda.Domain;

namespace BajkoweCuda.Infrastructure.Emails;

/// <summary>
/// Renders a transactional email in the recipient's language: texts from <c>EmailTexts.*.resx</c>, a small HTML
/// layout and a plain-text alternative (docs/PLAN.md §3.6). Every value is HTML-encoded.
/// </summary>
public static class EmailRenderer
{
    public const string LinkKey = "link";

    private static readonly ResourceManager Texts =
        new("BajkoweCuda.Infrastructure.Emails.EmailTexts", typeof(EmailRenderer).Assembly);

    private static readonly HtmlEncoder Html = HtmlEncoder.Default;

    public static RenderedEmail Render(
        string to,
        EmailTemplate template,
        string language,
        IReadOnlyDictionary<string, string> payload)
    {
        var lang = Languages.Find(language) ?? Languages.Default;
        var culture = CultureInfo.GetCultureInfo(lang);
        string Text(string key) =>
            Texts.GetString(key, culture) ?? throw new InvalidOperationException($"Missing email text '{key}'.");

        var subject = Text($"{template}_Subject");
        var heading = Text($"{template}_Heading");
        var body = Text($"{template}_Body");
        var action = Texts.GetString($"{template}_Action", culture);
        var link = payload.TryGetValue(LinkKey, out var value) ? RequireWebLink(value) : null;
        if (action is not null && link is null)
        {
            throw new InvalidOperationException($"The {template} email needs a '{LinkKey}' value.");
        }

        var html = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{lang}\"><head><meta charset=\"utf-8\"><title>{Html.Encode(subject)}</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#fdf8f3;font-family:Arial,Helvetica,sans-serif;color:#3b2f2a\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px\">")
            .Append(CultureInfo.InvariantCulture, $"<h1 style=\"font-size:22px;margin:0 0 16px\">{Html.Encode(heading)}</h1>")
            .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:16px;line-height:1.5\">{Html.Encode(body)}</p>");

        var text = new StringBuilder().AppendLine(heading).AppendLine().AppendLine(body);

        if (action is not null && link is not null)
        {
            var encodedLink = Html.Encode(link);
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:24px 0\"><a href=\"{encodedLink}\" style=\"display:inline-block;background:#b5543c;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px\">{Html.Encode(action)}</a></p>")
                .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;color:#6b5b53\">{Html.Encode(Text("Common_LinkFallback"))}<br><a href=\"{encodedLink}\" style=\"color:#6b5b53;word-break:break-all\">{encodedLink}</a></p>")
                .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;color:#6b5b53\">{Html.Encode(Text("Common_IgnoreIfNotYou"))}</p>");
            text.AppendLine()
                .AppendLine(CultureInfo.InvariantCulture, $"{action}: {link}")
                .AppendLine()
                .AppendLine(Text("Common_IgnoreIfNotYou"));
        }

        var signature = Text("Common_Signature");
        html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:14px;margin-top:32px\">{Html.Encode(signature)}</p></div></body></html>");
        text.AppendLine().AppendLine(signature);

        return new RenderedEmail(to, subject, html.ToString(), text.ToString());
    }

    /// <summary>Links in emails are always absolute http(s) URLs built by the application.</summary>
    private static string RequireWebLink(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.AbsoluteUri
            : throw new InvalidOperationException("An email link must be an absolute http(s) URL.");
}
