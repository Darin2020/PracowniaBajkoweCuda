using System.IO.Compression;
using Microsoft.AspNetCore.ResponseCompression;

namespace BajkoweCuda.Api.Hosting;

internal static class PublicSiteExtensions
{
    /// <summary>
    /// Text formats of the static site. JSON is deliberately absent: API responses are not compressed,
    /// which keeps compression side channels (BREACH) away from anything user-specific.
    /// </summary>
    private static readonly string[] CompressibleMimeTypes =
    [
        "text/html",
        "text/css",
        "text/javascript",
        "application/javascript",
        "image/svg+xml",
        "text/plain",
        "text/xml",
        "application/xml",
    ];

    public static IServiceCollection AddPublicSite(this IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = CompressibleMimeTypes;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
        });
        services.Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        return services;
    }

    /// <summary>Serves the prerendered Angular site from <c>wwwroot</c>: language redirect, pages, assets.</summary>
    public static IApplicationBuilder UsePublicSite(this IApplicationBuilder app)
    {
        app.UseResponseCompression();
        app.UseMiddleware<LocalizedSiteMiddleware>();
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = StaticAssetCaching.Apply });

        return app;
    }
}
