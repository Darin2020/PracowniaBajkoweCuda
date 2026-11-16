using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace BajkoweCuda.Api.Hosting;

/// <summary>Cache headers for the static site (docs/PLAN.md §4.4).</summary>
internal static partial class StaticAssetCaching
{
    /// <summary>HTML must be revalidated on every visit, so a new deployment is picked up immediately.</summary>
    public const string Html = "no-cache";

    /// <summary>Files whose name contains a content hash never change, so they can be cached forever.</summary>
    public const string Hashed = "public, max-age=31536000, immutable";

    /// <summary>Unhashed assets (icons, placeholder images, robots.txt): cached briefly.</summary>
    public const string Other = "public, max-age=3600";

    public static void Apply(StaticFileResponseContext context)
    {
        context.Context.Response.Headers.CacheControl = For(context.File.Name);
    }

    public static string For(string fileName)
    {
        if (fileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            return Html;
        }

        return HashedFileName().IsMatch(fileName) ? Hashed : Other;
    }

    /// <summary>Matches Angular's hashed bundles, for example <c>main-KRBIKYHT.js</c> or <c>chunk-BxRInVTD.js</c>.</summary>
    [GeneratedRegex(@"-[A-Za-z0-9_-]{8}\.(js|css)$", RegexOptions.CultureInvariant)]
    private static partial Regex HashedFileName();
}
