namespace BajkoweCuda.Domain;

/// <summary>
/// Languages of the site, the emails and the system messages (docs/PLAN.md §7). English is the source language.
/// Keep in step with <c>SUPPORTED_LOCALES</c> in the frontend.
/// </summary>
public static class Languages
{
    public const string Default = "en";

    public static IReadOnlyList<string> All { get; } = ["en", "de", "fr", "es", "pl"];

    /// <summary>Returns the supported language equal to <paramref name="value"/>, or <c>null</c>. Matching is exact (lower case).</summary>
    public static string? Find(ReadOnlySpan<char> value)
    {
        foreach (var language in All)
        {
            if (value.SequenceEqual(language))
            {
                return language;
            }
        }

        return null;
    }
}
