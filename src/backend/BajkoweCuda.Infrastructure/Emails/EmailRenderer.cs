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

    /// <summary>A longer text shown as its own block under the body, line breaks kept (the payment instructions).</summary>
    public const string DetailsKey = "details";

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

        // Texts may name payload values as {key} (for example {orderNumber}); the values are HTML-encoded below.
        string Fill(string text) => payload.Aggregate(text, (current, pair) => current.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal));
        var subject = Fill(Text($"{template}_Subject"));
        var heading = Fill(Text($"{template}_Heading"));
        var body = Fill(Text($"{template}_Body"));
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

        if (payload.TryGetValue(DetailsKey, out var details) && details.Length > 0)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:15px;line-height:1.5;padding:12px 16px;background:#fbeee4;border-radius:8px;white-space:pre-line\">{Html.Encode(details)}</p>");
            text.AppendLine().AppendLine(details);
        }

        // Account emails answer something the reader may not have asked for; order emails do not.
        var ignoreNote = template != EmailTemplate.OrderConfirmed;
        if (action is not null && link is not null)
        {
            var encodedLink = Html.Encode(link);
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:24px 0\"><a href=\"{encodedLink}\" style=\"display:inline-block;background:#b5543c;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px\">{Html.Encode(action)}</a></p>")
                .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;color:#6b5b53\">{Html.Encode(Text("Common_LinkFallback"))}<br><a href=\"{encodedLink}\" style=\"color:#6b5b53;word-break:break-all\">{encodedLink}</a></p>");
            text.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{action}: {link}");
            if (ignoreNote)
            {
                html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;color:#6b5b53\">{Html.Encode(Text("Common_IgnoreIfNotYou"))}</p>");
                text.AppendLine().AppendLine(Text("Common_IgnoreIfNotYou"));
            }
        }

        var signature = Text("Common_Signature");
        html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:14px;margin-top:32px\">{Html.Encode(signature)}</p></div></body></html>");
        text.AppendLine().AppendLine(signature);

        return new RenderedEmail(to, subject, html.ToString(), text.ToString());
    }

    /// <summary>
    /// The chat notification (§3.6): who wrote, an excerpt of each unread message (text is HTML-encoded), the number of
    /// photos, "order update" for system messages, and a link to the chat.
    /// </summary>
    public static RenderedEmail RenderChatDigest(
        string to,
        string language,
        bool forCustomer,
        string? customerName,
        IReadOnlyList<DigestItem> items,
        int moreCount,
        string link)
    {
        var lang = Languages.Find(language) ?? Languages.Default;
        var culture = CultureInfo.GetCultureInfo(lang);
        string Text(string key) =>
            Texts.GetString(key, culture) ?? throw new InvalidOperationException($"Missing email text '{key}'.");
        string Format(string key, object value) => string.Format(culture, Text(key), value);

        var name = customerName ?? Text("ChatDigest_UnknownCustomer");
        var subject = forCustomer ? Text("ChatDigest_Subject_Customer") : Format("ChatDigest_Subject_Studio", name);
        var heading = Text(forCustomer ? "ChatDigest_Heading_Customer" : "ChatDigest_Heading_Studio");
        var intro = forCustomer ? Text("ChatDigest_Intro_Customer") : Format("ChatDigest_Intro_Studio", name);
        var action = Text("ChatDigest_Action");
        var safeLink = RequireWebLink(link);
        var encodedLink = Html.Encode(safeLink);

        var html = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"<!doctype html><html lang=\"{lang}\"><head><meta charset=\"utf-8\"><title>{Html.Encode(subject)}</title></head>")
            .Append("<body style=\"margin:0;padding:24px;background:#fdf8f3;font-family:Arial,Helvetica,sans-serif;color:#3b2f2a\">")
            .Append("<div style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px;padding:32px\">")
            .Append(CultureInfo.InvariantCulture, $"<h1 style=\"font-size:22px;margin:0 0 16px\">{Html.Encode(heading)}</h1>")
            .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:16px;line-height:1.5\">{Html.Encode(intro)}</p>");
        var text = new StringBuilder().AppendLine(heading).AppendLine().AppendLine(intro).AppendLine();

        foreach (var item in items)
        {
            var parts = new List<string>();
            if (item.IsSystem)
            {
                var eventKey = item.SystemEvent is null ? null : "ChatDigest_" + item.SystemEvent.Replace('.', '_');
                parts.Add(eventKey is not null && item.OrderNumber is not null && Texts.GetString(eventKey, culture) is { } eventText
                    ? string.Format(culture, eventText, item.OrderNumber)
                    : Text("ChatDigest_OrderUpdate"));
            }
            else if (item.Excerpt.Length > 0)
            {
                parts.Add(item.Excerpt);
            }

            if (item.ImageCount > 0)
            {
                parts.Add(Format("ChatDigest_Photos", item.ImageCount));
            }

            var line = string.Join(" · ", parts);
            html.Append("<div style=\"margin:0 0 12px;padding:12px 16px;background:#fbeee4;border-radius:8px\">")
                .Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:15px;line-height:1.5;margin:0;white-space:pre-line\">{Html.Encode(line)}</p>");
            if (item.ImageContentIds is { Count: > 0 } images)
            {
                html.Append("<p style=\"margin:8px 0 0\">");
                foreach (var id in images)
                {
                    // Content ids are made by the application (letters, digits, dashes); encoded anyway.
                    html.Append(CultureInfo.InvariantCulture, $"<img src=\"cid:{Html.Encode(id)}\" alt=\"\" width=\"120\" style=\"width:120px;height:auto;border-radius:6px;margin:0 6px 6px 0;vertical-align:top\">");
                }

                html.Append("</p>");
            }

            html.Append("</div>");
            text.AppendLine(CultureInfo.InvariantCulture, $"> {line}").AppendLine();
        }

        if (moreCount > 0)
        {
            var more = Format("ChatDigest_More", moreCount);
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:14px;color:#6b5b53\">{Html.Encode(more)}</p>");
            text.AppendLine(more).AppendLine();
        }

        html.Append(CultureInfo.InvariantCulture, $"<p style=\"margin:24px 0\"><a href=\"{encodedLink}\" style=\"display:inline-block;background:#b5543c;color:#ffffff;text-decoration:none;padding:12px 20px;border-radius:8px\">{Html.Encode(action)}</a></p>");
        text.AppendLine(CultureInfo.InvariantCulture, $"{action}: {safeLink}");

        if (forCustomer)
        {
            var footer = Text("ChatDigest_Footer_Customer");
            html.Append(CultureInfo.InvariantCulture, $"<p style=\"font-size:13px;color:#6b5b53\">{Html.Encode(footer)}</p>");
            text.AppendLine().AppendLine(footer);
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

/// <summary>
/// One unread message in a chat notification: an excerpt, how many photos (and, for the first few, their thumbnails as
/// inline images), or an order update.
/// </summary>
public sealed record DigestItem(
    bool IsSystem,
    string Excerpt,
    int ImageCount,
    IReadOnlyList<string>? ImageContentIds = null,
    string? SystemEvent = null,
    string? OrderNumber = null);
