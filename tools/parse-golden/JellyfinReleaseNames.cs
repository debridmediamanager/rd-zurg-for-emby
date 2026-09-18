using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Emby.Naming.Common;
using Emby.Naming.TV;

namespace Jellyfin.Plugin.RdZurg.Library;

/// <summary>
/// Reads identity out of release names, using the same parsers the filesystem scanner uses.
/// </summary>
public sealed class ReleaseNames
{
    // Jellyfin's bare range expression, which is not anchored to anything.
    private const string BareRange = "([0-9]+)-([0-9]+)";

    // Refuses the digit after an audio channel decimal when a codec follows it, as in 5.1x265 or AAC2.0x264.
    private const string NotChannelDecimal = @"(?!(?<=(?:^|[^0-9])[0-9][.,])[0-9][xX]26[4-6](?![0-9]))";

    private static readonly string[] _videoExtensions =
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".mov", ".wmv", ".mpg", ".mpeg", ".flv", ".webm"
    };

    private readonly EpisodeResolver _episodes = new(CreateNamingOptions());

    // Jellyfin's stock options, used only to recognise what an earlier build filed.
    private readonly EpisodeResolver _stockEpisodes = new(new NamingOptions());

    /// <summary>Reports whether a path looks like a video file worth publishing.</summary>
    /// <param name="path">A path from a torrent's file list.</param>
    /// <returns><c>true</c> when the extension is one of the video extensions.</returns>
    /// <remarks>
    /// An archive-wrapped release still lists its inner name here, so <c>.mkv</c> covers the case
    /// where the link goes on to serve <c>.mkv.rar</c>.
    /// </remarks>
    public static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);

        foreach (var candidate in _videoExtensions)
        {
            if (string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads a file as an episode, when it is one.
    /// </summary>
    /// <param name="torrentName">The release name, which stands in for the containing folder.</param>
    /// <param name="filePath">The file's path within the torrent.</param>
    /// <returns>The episode, or <c>null</c> when the file is not one.</returns>
    /// <remarks>
    /// <para>
    /// Two things here are load bearing. Jellyfin's episode expressions take the series name from
    /// the containing folder, so a bare filename resolves a number and no series at all; the
    /// torrent name is synthesized as that folder to give the expressions the shape they expect.
    /// </para>
    /// <para>
    /// And the optimistic expressions are turned off, because they read a release year as a season
    /// and episode pair. Left on, <c>The.Matrix.Reloaded.2003.2160p</c> parses as season 20 episode
    /// 3 and a shelf of films becomes a shelf of one-episode shows.
    /// </para>
    /// </remarks>
    public EpisodeInfo? ParseEpisode(string torrentName, string filePath)
    {
        ArgumentNullException.ThrowIfNull(torrentName);
        ArgumentNullException.ThrowIfNull(filePath);

        var parsed = _episodes.Resolve(Synthesize(torrentName, filePath), false, isOptimistic: false);

        if (parsed?.SeasonNumber is null || parsed.EpisodeNumber is null || string.IsNullOrWhiteSpace(parsed.SeriesName))
        {
            return null;
        }

        return parsed;
    }

    /// <summary>
    /// Reports whether an episode an earlier build filed is a file this parser no longer reads as one.
    /// </summary>
    /// <param name="torrentName">The release name the file was parsed with.</param>
    /// <param name="filePath">The file's path or name.</param>
    /// <param name="season">The season the item was filed under.</param>
    /// <param name="episode">The episode number the item was filed under.</param>
    /// <returns>
    /// <c>true</c> only when Jellyfin's stock expressions read exactly this season and episode from these
    /// names and this parser reads no episode at all.
    /// </returns>
    /// <remarks>
    /// Reproducing the item's own numbers is what makes acting on the answer safe. It proves the item came
    /// from these names through the expressions this parser dropped, so an episode read some other way is
    /// never touched, and a file let go of here cannot be read back as the same episode by the next pass.
    /// </remarks>
    public bool IsMisreadEpisode(string torrentName, string filePath, int? season, int? episode)
    {
        ArgumentNullException.ThrowIfNull(torrentName);
        ArgumentNullException.ThrowIfNull(filePath);

        if (season is null || episode is null || ParseEpisode(torrentName, filePath) is not null)
        {
            return false;
        }

        var stock = _stockEpisodes.Resolve(Synthesize(torrentName, filePath), false, isOptimistic: false);
        return stock?.SeasonNumber == season
            && stock.EpisodeNumber == episode
            && !string.IsNullOrWhiteSpace(stock.SeriesName);
    }

    /// <summary>
    /// Builds Jellyfin's naming options without the two episode shapes that read a film's own tags as a
    /// season and episode.
    /// </summary>
    /// <returns>The options the episode resolver runs with.</returns>
    /// <remarks>
    /// <para>
    /// The bare <c>([0-9]+)-([0-9]+)</c> expression matches anywhere in the folder or the file name, so
    /// <c>AC3-2.0</c>, <c>RIFE.4.17-60fps</c>, <c>2026-1080p</c> and a collection's <c>1999-2021</c> each
    /// become a season and an episode. It is dropped.
    /// </para>
    /// <para>
    /// The <c>NxNN</c> expressions read <c>DTS.XLL.5.1x265</c> as season 1 episode 265. They are kept, but
    /// refuse the digit after an audio channel decimal when <c>x264</c>, <c>x265</c> or <c>x266</c> follows,
    /// so <c>2011.1x01</c>, <c>Blakes.7.1x01</c>, <c>MythBusters.2005x17</c> and <c>Formula.1.2024x27</c>
    /// still read as episodes.
    /// </para>
    /// </remarks>
    public static NamingOptions CreateNamingOptions()
    {
        var options = new NamingOptions();
        options.EpisodeExpressions = options.EpisodeExpressions
            .Where(e => !string.Equals(e.Expression, BareRange, StringComparison.Ordinal))
            .Select(GuardChannelDecimals)
            .ToArray();
        return options;
    }

    private static EpisodeExpression GuardChannelDecimals(EpisodeExpression expression)
    {
        var guarded = expression.Expression
            .Replace(@"[\\\/\._ \[\(-]([0-9]+)x", @"[\\\/\._ \[\(-]" + NotChannelDecimal + "([0-9]+)x", StringComparison.Ordinal)
            .Replace(
                @"([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]+))",
                NotChannelDecimal + @"([sS]?(?<seasonnumber>[0-9]{1,4})[xX](?<epnumber>[0-9]+))",
                StringComparison.Ordinal);

        if (string.Equals(guarded, expression.Expression, StringComparison.Ordinal))
        {
            return expression;
        }

        return new EpisodeExpression(guarded, expression.IsByDate)
        {
            IsOptimistic = expression.IsOptimistic,
            IsNamed = expression.IsNamed,
            SupportsAbsoluteEpisodeNumbers = expression.SupportsAbsoluteEpisodeNumbers,
            DateTimeFormats = expression.DateTimeFormats
        };
    }

    private static string Synthesize(string torrentName, string filePath)
        => string.Format(
            CultureInfo.InvariantCulture,
            "/{0}/{1}",
            torrentName.Replace('/', '_'),
            Path.GetFileName(filePath));

    /// <summary>Turns a dotted release name into something a metadata provider can search for.</summary>
    /// <param name="value">A release name or series name.</param>
    /// <returns>The name with separators turned back into spaces.</returns>
    public static string Humanise(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace('.', ' ').Replace('_', ' ').Trim();
    }
}
