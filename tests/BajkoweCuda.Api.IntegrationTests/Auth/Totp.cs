using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>An authenticator app for tests: RFC 6238 codes (HMAC-SHA1, 30-second steps, 6 digits), as Identity checks them.</summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <param name="sharedKey">The key from <c>GET /api/auth/2fa/setup</c> (any case, spaces allowed).</param>
    /// <param name="stepOffset">
    /// Steps of 30 seconds from now. Identity accepts codes a few steps around the current time, so a test can use a
    /// different step for each sign-in and never reuse a code.
    /// </param>
    public static string Code(string sharedKey, int stepOffset = 0)
    {
        var step = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30) + stepOffset;
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);

#pragma warning disable CA5350 // RFC 6238 authenticator codes are HMAC-SHA1, like Identity's and every authenticator app.
        var hash = HMACSHA1.HashData(FromBase32(sharedKey), counter);
#pragma warning restore CA5350
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] FromBase32(string text)
    {
        var clean = text.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var character in clean)
        {
            buffer = (buffer << 5) | Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. output];
    }
}
