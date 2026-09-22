using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>
/// Where a release is published inside the tree.
/// </summary>
/// <remarks>
/// <para>
/// Emby reads identity back out of these names, so they are written the way it expects: a movie folder named
/// "Title (Year)" holding "Title (Year) - label.strm" files, one per release, which Emby groups as versions of
/// one film; an episode named "Series - SxxEyy - label.strm" under "Series/Season NN".
/// </para>
/// <para>
/// Releases without a year never share a folder. Emby merges by folder, and "One Piece" episodes that carry no
/// season marker all parse to the same title; the Jellyfin plugin measured 155 of them folded into one film.
/// </para>
/// </remarks>
public static class StrmPaths
{
    /// <summary>The folder holding films.</summary>
    public const string Movies = "movies";

    /// <summary>The folder holding shows.</summary>
    public const string Shows = "shows";

    /// <summary>Emby stops grouping a folder's files as versions of one item past this many.</summary>
    /// <remarks>Measured on Emby 4.10: eight files group into one item, nine become nine items.</remarks>
    public const int MaxVersions = 8;

    // What one path component may take, leaving room for ".strm" and for filesystems that count differently.
    private const int ComponentBytes = 200;

    // What a folder name may take. A file is named after its folder and then says which version or which episode it
    // is, so the folder leaves room for that: cut at the full budget, the episode number or the tag that tells two
    // releases apart was the part that fell off.
    private const int FolderBytes = 150;

    private static readonly char[] _invalid = "<>:\"/\\|?*".ToCharArray();

    // Tokens that make Emby read a file as something other than the release: an extra, a disc of a stack, a 3D
    // rip, or a second episode.
    private static readonly Regex _labelNoise = new(
        @"(?:^|[\s._-])(?:sample|trailer|scene|clip|behindthescenes|deleted(?:scene)?|featurette|short|interview|extra|other|cd[0-9]{1,2}|dis[ck][\s._-]*[0-9a-d]|p(?:ar)?t[\s._-]*[0-9ivx]{1,3}|3d|sbs|hsbs|tab|htab|mvc)(?=$|[\s._-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _labelMarker = new(
        @"(?:^|[\s._-])(?:s[0-9]{1,4}[\s._-]*e[0-9]{1,4}(?:[-e][0-9]{1,4})*|[0-9]{1,4}x[0-9]{1,4})(?=$|[\s._-])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _spaces = new(@"\s{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The folder a film's versions live in.</summary>
    /// <param name="title">The film's title.</param>
    /// <param name="year">Its year, when the release carries one.</param>
    /// <param name="key">The content key, which keeps a yearless release out of anyone else's folder.</param>
    /// <returns>A folder name.</returns>
    public static string MovieFolder(string title, int? year, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // The year is what Emby searches with and the tag is what keeps a yearless release to itself, so the title
        // yields to them rather than the other way round. Emby strips a bracketed tag before searching, so the
        // folder still reads as the title.
        var suffix = year is int value
            ? string.Format(CultureInfo.InvariantCulture, " ({0})", value)
            : string.Format(CultureInfo.InvariantCulture, " [{0}]", key);
        var clean = Truncate(Component(title, fallback: key), FolderBytes - Encoding.UTF8.GetByteCount(suffix));
        return Component(clean + suffix, key);
    }

    /// <summary>The file a film release is published at.</summary>
    /// <param name="folder">The folder from <see cref="MovieFolder"/>.</param>
    /// <param name="label">The version label, from <see cref="Label"/>.</param>
    /// <returns>A path relative to the tree root.</returns>
    /// <param name="tag">A tag that tells this file apart from another with the same name, kept whole when the name is cut.</param>
    public static string MoviePath(string folder, string label, string? tag = null)
        => string.Format(CultureInfo.InvariantCulture, "{0}/{1}/{2}", Movies, folder, Tagged(folder + " - " + label, folder, tag) + ".strm");

    /// <summary>The folder a series' episodes live in.</summary>
    /// <param name="series">The series title.</param>
    /// <param name="key">The content key, used when the series has no usable name.</param>
    /// <returns>A folder name.</returns>
    public static string SeriesFolder(string series, string key) => Truncate(Component(series, key), FolderBytes);

    /// <summary>The file an episode is published at.</summary>
    /// <param name="series">The series title.</param>
    /// <param name="season">The season number.</param>
    /// <param name="episode">The episode number.</param>
    /// <param name="endingEpisode">The last episode of a multi-episode file, if any.</param>
    /// <param name="label">The version label, from <see cref="Label"/>.</param>
    /// <param name="key">The content key, used when the series has no usable name.</param>
    /// <param name="tag">A tag that tells this file apart from another with the same name, kept whole when the name is cut.</param>
    /// <returns>A path relative to the tree root.</returns>
    public static string EpisodePath(string series, int season, int episode, int? endingEpisode, string label, string key, string? tag = null)
    {
        var folder = SeriesFolder(series, key);
        var numbers = endingEpisode is int end
            ? string.Format(CultureInfo.InvariantCulture, "S{0:D2}E{1:D2}-E{2:D2}", season, episode, end)
            : string.Format(CultureInfo.InvariantCulture, "S{0:D2}E{1:D2}", season, episode);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/{1}/Season {2:D2}/{3}",
            Shows,
            folder,
            season,
            Tagged(string.Format(CultureInfo.InvariantCulture, "{0} - {1} - {2}", folder, numbers, label), folder + " - " + numbers, tag) + ".strm");
    }

    // A tag appended after the cut would be cut with it, and two names that only differed past the budget would
    // collide again. So the name is cut short enough to leave the tag whole.
    private static string Tagged(string name, string fallback, string? tag)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return Component(name, fallback);
        }

        var suffix = " [" + tag + "]";
        return Truncate(Component(name, fallback), ComponentBytes - Encoding.UTF8.GetByteCount(suffix)) + suffix;
    }

    /// <summary>
    /// The label Emby shows for one version of an item, taken from the release's own file name.
    /// </summary>
    /// <param name="fileName">The file's name inside the torrent.</param>
    /// <param name="key">The content key, appended when the label would otherwise be empty.</param>
    /// <returns>A label.</returns>
    /// <remarks>
    /// Emby parses the label too, so anything in it that reads as an extra, a stacked disc, a 3D rip or another
    /// episode has to go, or the release is filed as something other than what it is.
    /// </remarks>
    public static string Label(string fileName, string key)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = ReleaseNames.Humanise(System.IO.Path.GetFileNameWithoutExtension(fileName));
        name = _labelMarker.Replace(name, " ");
        name = _labelNoise.Replace(name, " ");
        name = _spaces.Replace(name, " ").Trim(' ', '-', '_', '.');
        return name.Length > 0 ? name : key;
    }

    /// <summary>Makes one path component safe on every filesystem Emby runs on.</summary>
    /// <param name="value">The component.</param>
    /// <param name="fallback">What to use when nothing usable is left.</param>
    /// <returns>The component.</returns>
    public static string Component(string value, string fallback)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(fallback);

        var cleaned = new string(value.Select(c => _invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        cleaned = _spaces.Replace(cleaned, " ").Trim();

        // A leading dot hides the file; a trailing dot or space is dropped by Windows and confuses everything else.
        cleaned = cleaned.TrimStart('.').TrimEnd('.', ' ').Trim();
        cleaned = Truncate(cleaned, ComponentBytes);

        return cleaned.Length > 0 ? cleaned : Truncate(fallback, ComponentBytes);
    }

    /// <summary>Cuts a component to a byte budget without splitting a character.</summary>
    /// <param name="value">The component.</param>
    /// <param name="maxBytes">The budget.</param>
    /// <returns>The component.</returns>
    public static string Truncate(string value, int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        var bytes = 0;
        var end = 0;
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(value);
        while (enumerator.MoveNext())
        {
            var element = (string)enumerator.Current;
            var size = Encoding.UTF8.GetByteCount(element);
            if (bytes + size > maxBytes)
            {
                break;
            }

            bytes += size;
            end += element.Length;
        }

        return value[..end].TrimEnd('.', ' ');
    }
}
