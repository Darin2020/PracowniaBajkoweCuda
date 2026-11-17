using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BajkoweCuda.Api.IntegrationTests.PublicSite;

/// <summary>
/// Runs the API over a small fake build of the Angular site, shaped like the real output:
/// one folder per locale with a prerendered landing page, a prerendered legal page, the client-side shell,
/// hashed bundles and an unhashed asset, plus the root files. As in the real build, absolute URLs are
/// written with the placeholder origin, which the API must replace with <see cref="PublicOrigin"/>.
/// </summary>
public sealed class SiteFactory : WebApplicationFactory<Program>
{
    public const string PublicOrigin = "https://toys.test";
    public const string AppVersion = "0123abc";

    /// <summary>Must equal <c>PublicSiteOptions.OriginPlaceholder</c> and the frontend's <c>PUBLIC_BASE_URL_PLACEHOLDER</c>.</summary>
    public const string OriginPlaceholder = "https://public-base-url.invalid";

    public static readonly IReadOnlyList<string> Locales = ["en", "de", "fr", "es", "pl"];

    private readonly string _webRoot = Directory.CreateTempSubdirectory("bajkowecuda-wwwroot-").FullName;

    public SiteFactory()
    {
        foreach (var locale in Locales)
        {
            Write($"{locale}/index.html", Landing(locale, OriginPlaceholder));
            Write($"{locale}/index.csr.html", Shell(locale));
            Write($"{locale}/privacy/index.html", Privacy(locale, OriginPlaceholder));
            Write($"{locale}/main-KRBIKYHT.js", $"console.log('{OriginPlaceholder}');");
            Write($"{locale}/chunk-BxRInVTD.js", "console.log('chunk');");
            Write($"{locale}/styles-VN3VGPQH.css", "body{margin:0}");
            Write($"{locale}/icon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        }

        Write("robots.txt", Robots(OriginPlaceholder));
        Write("sitemap.xml", Sitemap(OriginPlaceholder));
    }

    public static string Landing(string locale, string origin = PublicOrigin) =>
        $"<!doctype html><html lang=\"{locale}\"><head><link rel=\"canonical\" href=\"{origin}/{locale}/\">" +
        $"<meta property=\"og:image\" content=\"{origin}/{locale}/og-image.png\"></head><body>landing {locale} żółć</body></html>";

    public static string Shell(string locale) => $"<!doctype html><html lang=\"{locale}\"><body>shell {locale}</body></html>";

    public static string Privacy(string locale, string origin = PublicOrigin) =>
        $"<!doctype html><html lang=\"{locale}\"><head><link rel=\"canonical\" href=\"{origin}/{locale}/privacy\"></head><body>privacy {locale}</body></html>";

    public static string Robots(string origin = PublicOrigin) => $"User-agent: *\nDisallow: /api/\n\nSitemap: {origin}/sitemap.xml\n";

    public static string Sitemap(string origin = PublicOrigin) => $"<urlset><url><loc>{origin}/en/</loc></url></urlset>";

    public HttpClient CreateSiteClient() => CreateSiteClient(this);

    /// <summary>The same site with extra configuration, for example <c>Seo:NoIndex</c>.</summary>
    public HttpClient CreateSiteClient(params (string Key, string Value)[] settings)
    {
        var factory = WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

        return CreateSiteClient(factory);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseWebRoot(_webRoot);
        builder.UseSetting("PublicBaseUrl", PublicOrigin);
        builder.UseSetting("APP_VERSION", AppVersion);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private static HttpClient CreateSiteClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
