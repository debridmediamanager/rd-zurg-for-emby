using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>Scopes playback URLs to one file and the Real-Debrid account they were published from.</summary>
/// <remarks>
/// The scope is the account's id, not its token: a <c>.strm</c> file holds the signed URL, so a scope that changed
/// with every token rotation would rewrite the whole library each time. Switching to another account, or changing
/// the signing key, still revokes every old URL.
/// </remarks>
public static class StreamAccess
{
    /// <summary>Reports whether a value is a canonical Real-Debrid content key.</summary>
    /// <param name="key">The candidate key.</param>
    /// <returns>Whether it has exactly 13 uppercase ASCII letters or digits.</returns>
    public static bool IsValidKey(string key)
        => key.Length == 13 && key.All(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9');

    /// <summary>Signs a single content key for one account.</summary>
    /// <param name="secret">The hex signing key.</param>
    /// <param name="accountId">The Real-Debrid account id.</param>
    /// <param name="key">The content key.</param>
    /// <returns>A hexadecimal signature.</returns>
    public static string Sign(string secret, string accountId, string key)
        => Convert.ToHexString(HMACSHA256.HashData(Convert.FromHexString(secret), Encoding.UTF8.GetBytes(accountId + "\n" + key)));

    /// <summary>Checks a capability before any provider request or cache lookup.</summary>
    /// <param name="secret">The hex signing key.</param>
    /// <param name="accountId">The account the library is published from.</param>
    /// <param name="key">The requested content key.</param>
    /// <param name="signature">The supplied signature.</param>
    /// <returns>Whether the signature authorizes this content key.</returns>
    public static bool Verify(string secret, string accountId, string key, string? signature)
    {
        if (!IsValidKey(key) || signature is not { Length: 64 } || string.IsNullOrWhiteSpace(accountId) || secret is not { Length: 64 })
        {
            return false;
        }

        return TryDecode32(signature, out var supplied)
            && TryDecode32(secret, out _)
            && CryptographicOperations.FixedTimeEquals(supplied, Convert.FromHexString(Sign(secret, accountId, key)));
    }

    /// <summary>Decodes exactly 32 bytes of hex without throwing on anything else.</summary>
    /// <param name="hex">The candidate.</param>
    /// <param name="bytes">The bytes, when it decodes.</param>
    /// <returns>Whether it was 64 hex digits.</returns>
    public static bool TryDecode32(string? hex, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (hex is not { Length: 64 } || !hex.All(Uri.IsHexDigit))
        {
            return false;
        }

        bytes = Convert.FromHexString(hex);
        return true;
    }
}
