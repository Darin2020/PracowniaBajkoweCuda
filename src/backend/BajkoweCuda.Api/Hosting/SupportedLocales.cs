using BajkoweCuda.Domain;

namespace BajkoweCuda.Api.Hosting;

/// <summary>
/// Locales the site is built for. Each one is a separately built Angular app in <c>wwwroot/{locale}</c>,
/// served under the <c>/{locale}/</c> URL prefix. They are the supported <see cref="Languages"/>.
/// </summary>
internal static class SupportedLocales
{
    public const string Default = Languages.Default;

    public static IReadOnlyList<string> All => Languages.All;

    /// <summary>Returns the supported locale equal to <paramref name="value"/>, or <c>null</c>. Matching is exact (lower case).</summary>
    public static string? Find(ReadOnlySpan<char> value) => Languages.Find(value);
}
