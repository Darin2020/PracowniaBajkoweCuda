using System.Security.Cryptography;

namespace BajkoweCuda.Api.Security;

/// <summary>
/// A fresh random value per response that lets the page's own <c>&lt;style&gt;</c> elements through the CSP
/// (<c>style-src 'nonce-…'</c>) without <c>'unsafe-inline'</c>. The Angular build writes <see cref="Placeholder"/>
/// into every HTML file (<c>ngCspNonce</c> on the root element, <c>nonce</c> on styles and scripts); the server
/// replaces it when it sends a page (docs/DECISIONS.md).
/// </summary>
internal static class CspNonce
{
    /// <summary>Must equal the <c>ngCspNonce</c> value in <c>src/frontend/src/index.html</c>.</summary>
    public const string Placeholder = "__CSP_NONCE__";

    private static readonly object Key = new();

    /// <summary>The nonce of this response, created on first use.</summary>
    public static string For(HttpContext context)
    {
        if (context.Items.TryGetValue(Key, out var value) && value is string nonce)
        {
            return nonce;
        }

        // 128 bits from the CSPRNG, Base64 as the CSP grammar expects.
        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[Key] = nonce;
        return nonce;
    }
}
