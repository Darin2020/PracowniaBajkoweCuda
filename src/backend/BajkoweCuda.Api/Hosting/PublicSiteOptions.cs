namespace BajkoweCuda.Api.Hosting;

/// <summary>Per-environment settings of the public site (docs/PLAN.md §14).</summary>
internal sealed class PublicSiteOptions
{
    public const string PublicBaseUrlKey = "PublicBaseUrl";
    public const string NoIndexKey = "Seo:NoIndex";

    /// <summary>
    /// Origin the Angular build writes wherever an absolute URL of the site is needed (canonical, hreflang,
    /// Open Graph, JSON-LD, robots.txt, sitemap.xml). The image is built once and promoted between environments,
    /// so the real origin is only known when the app starts; <see cref="PublicSiteFiles"/> swaps it in.
    /// Keep in step with <c>PUBLIC_BASE_URL_PLACEHOLDER</c> in the frontend.
    /// </summary>
    public const string OriginPlaceholder = "https://public-base-url.invalid";

    /// <summary>Public origin of this environment without a trailing slash, for example <c>https://example.com</c>.</summary>
    public required string Origin { get; init; }

    /// <summary>True on environments that search engines must not index (staging).</summary>
    public bool NoIndex { get; init; }

    /// <summary>Reads and validates the settings. Throws at startup, so a misconfigured deployment never serves pages.</summary>
    public static PublicSiteOptions FromConfiguration(IConfiguration configuration)
    {
        var value = configuration[PublicBaseUrlKey];

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.AbsolutePath != "/"
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException(
                $"Configuration value '{PublicBaseUrlKey}' must be the public origin of the site, " +
                "for example 'https://example.com' (http or https, no path, query or credentials).");
        }

        return new PublicSiteOptions
        {
            Origin = uri.GetLeftPart(UriPartial.Authority),
            NoIndex = configuration.GetValue<bool>(NoIndexKey),
        };
    }
}
