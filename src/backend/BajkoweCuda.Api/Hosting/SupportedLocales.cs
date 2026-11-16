namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Locales the site is built for. Each one is a separately built Angular app in <c>wwwroot/{locale}</c>,
/// served under the <c>/{locale}/</c> URL prefix. Keep in step with <c>SUPPORTED_LOCALES</c> in the frontend.
/// </summary>
internal static class SupportedLocales
{
    public const string Default = "en";

    public static IReadOnlyList<string> All { get; } = ["en", "de", "fr", "es", "pl"];

    /// <summary>Returns the supported locale equal to <paramref name="value"/>, or <c>null</c>. Matching is exact (lower case).</summary>
    public static string? Find(ReadOnlySpan<char> value)
    {
        foreach (var locale in All)
        {
            if (value.SequenceEqual(locale))
            {
                return locale;
            }
        }

        return null;
    }
}
