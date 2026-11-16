using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BajkoweCuda.Api.IntegrationTests.PublicSite;

/// <summary>
/// Runs the API over a small fake build of the Angular site, shaped like the real output:
/// one folder per locale with a prerendered landing page, a prerendered legal page, the client-side shell,
/// hashed bundles and an unhashed asset, plus the root files.
/// </summary>
public sealed class SiteFactory : WebApplicationFactory<Program>
{
    public static readonly IReadOnlyList<string> Locales = ["en", "de", "fr", "es", "pl"];

    private readonly string _webRoot = Directory.CreateTempSubdirectory("bajkowecuda-wwwroot-").FullName;

    public SiteFactory()
    {
        foreach (var locale in Locales)
        {
            Write($"{locale}/index.html", Landing(locale));
            Write($"{locale}/index.csr.html", Shell(locale));
            Write($"{locale}/privacy/index.html", Privacy(locale));
            Write($"{locale}/main-KRBIKYHT.js", "console.log('main');");
            Write($"{locale}/chunk-BxRInVTD.js", "console.log('chunk');");
            Write($"{locale}/styles-VN3VGPQH.css", "body{margin:0}");
            Write($"{locale}/icon.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
        }

        Write("robots.txt", "User-agent: *\n");
        Write("sitemap.xml", "<urlset/>");
    }

    public static string Landing(string locale) => $"<!doctype html><html lang=\"{locale}\"><body>landing {locale}</body></html>";

    public static string Shell(string locale) => $"<!doctype html><html lang=\"{locale}\"><body>shell {locale}</body></html>";

    public static string Privacy(string locale) => $"<!doctype html><html lang=\"{locale}\"><body>privacy {locale}</body></html>";

    public HttpClient CreateSiteClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseWebRoot(_webRoot);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_webRoot))
        {
            Directory.Delete(_webRoot, recursive: true);
        }
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_webRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
