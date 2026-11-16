using Microsoft.Net.Http.Headers;

namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Picks the language for a visitor who arrives at <c>/</c>:
/// the <c>lang</c> cookie (set by the language switcher), then <c>Accept-Language</c>, then English.
/// </summary>
internal static class LanguageNegotiator
{
    public const string CookieName = "lang";

    public static string Negotiate(HttpRequest request)
    {
        if (request.Cookies.TryGetValue(CookieName, out var cookie) && SupportedLocales.Find(cookie) is { } fromCookie)
        {
            return fromCookie;
        }

        return FromAcceptLanguage(request) ?? SupportedLocales.Default;
    }

    private static string? FromAcceptLanguage(HttpRequest request)
    {
        var header = request.Headers.AcceptLanguage;

        if (header.Count == 0 || !StringWithQualityHeaderValue.TryParseList(header, out var values))
        {
            return null;
        }

        // Stable sort: equal quality keeps the order the browser sent.
        foreach (var value in values.Where(value => value.Quality is null or > 0).OrderByDescending(value => value.Quality ?? 1))
        {
            // "fr-CH" and "FR" both mean French here: only the primary language subtag matters.
            var tag = value.Value.AsSpan();
            var dash = tag.IndexOf('-');
            var primary = (dash < 0 ? tag : tag[..dash]).ToString().ToLowerInvariant();

            if (SupportedLocales.Find(primary) is { } locale)
            {
                return locale;
            }
        }

        return null;
    }
}
