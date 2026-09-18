using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>A resolved half of a Range header: a concrete first and last byte.</summary>
public readonly record struct ByteRange(long From, long To)
{
    /// <summary>Gets how many bytes the range covers.</summary>
    public long Length => (To - From) + 1;

    /// <summary>
    /// Reads a single-range <c>Range</c> header against a known length.
    /// </summary>
    /// <param name="header">The header value, for example <c>bytes=0-1023</c>.</param>
    /// <param name="length">The length of the thing being read.</param>
    /// <param name="range">The resolved range.</param>
    /// <returns><c>true</c> when the header names a satisfiable range.</returns>
    /// <remarks>
    /// Only the single-range form is handled, which is the only form players send. A suffix range
    /// (<c>bytes=-500</c>) means the last 500 bytes, and an open range (<c>bytes=500-</c>) runs to
    /// the end.
    /// </remarks>
    public static bool TryParse(string? header, long length, [NotNullWhen(true)] out ByteRange? range)
    {
        range = null;

        if (length <= 0 || string.IsNullOrWhiteSpace(header))
        {
            return false;
        }

        const string Prefix = "bytes=";
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var spec = header.AsSpan(Prefix.Length).Trim();
        if (spec.Contains(','))
        {
            return false;
        }

        var dash = spec.IndexOf('-');
        if (dash < 0)
        {
            return false;
        }

        var fromText = spec[..dash].Trim();
        var toText = spec[(dash + 1)..].Trim();

        long from;
        long to;

        if (fromText.IsEmpty)
        {
            if (!long.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix <= 0)
            {
                return false;
            }

            from = Math.Max(0, length - suffix);
            to = length - 1;
        }
        else
        {
            if (!long.TryParse(fromText, NumberStyles.None, CultureInfo.InvariantCulture, out from) || from >= length)
            {
                return false;
            }

            if (toText.IsEmpty)
            {
                to = length - 1;
            }
            else if (!long.TryParse(toText, NumberStyles.None, CultureInfo.InvariantCulture, out to))
            {
                return false;
            }

            to = Math.Min(to, length - 1);
        }

        if (to < from)
        {
            return false;
        }

        range = new ByteRange(from, to);
        return true;
    }
}
