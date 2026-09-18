using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Plugin.RdZurg.Naming;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>What a file's name says about the episode it holds.</summary>
/// <param name="SeriesName">The series name, as the release spells it.</param>
/// <param name="SeasonNumber">The season.</param>
/// <param name="EpisodeNumber">The first episode.</param>
/// <param name="EndingEpisodeNumber">The last episode of a multi-episode file, if any.</param>
public sealed record EpisodeName(string SeriesName, int SeasonNumber, int EpisodeNumber, int? EndingEpisodeNumber);

/// <summary>
/// Reads identity out of release names.
/// </summary>
/// <remarks>
/// The expressions are the MIT-licensed original Emby.Naming (vendored under <c>Naming/</c>) with this plugin's
/// changes applied here. Classification decides the file's path, and a path is an Emby item's identity, so it
/// must not move when Emby's own parser changes; that is why the parser ships inside the plugin.
/// </remarks>
public sealed class ReleaseNames
{
    // The bare range expression, which is not anchored to anything.
    private const string BareRange = "([0-9]+)-([0-9]+)";

    // Refuses the digit after an audio channel decimal when a codec follows it, as in 5.1x265 or AAC2.0x264.
    private const string NotChannelDecimal = @"(?!(?<=(?:^|[^0-9])[0-9][.,])[0-9][xX]26[4-6](?![0-9]))";

    // Where upstream's NxNN expressions read a season number; the guard goes in front of each.
    private const string UnnamedSeason = @"[\\/\._ \[\(-]([0-9]+)x";
    private const string NamedSeason = @"[sS]?(?<seasonnumber>\d{1,4})[xX]";

    // How many upstream expressions each edit must touch. A count that drifts means an edit silently stopped
    // applying, which is how a guard written against one parser's spelling does nothing on another's.
    private const int ExpectedRangesRemoved = 1;
    private const int ExpectedEpisodeGuards = 3;
    private const int ExpectedMultipleEpisodeGuards = 8;

    // "Show Season 1 Episode 2", "Show (2025) Season 1 Episode 2- Title", "Season 01 Episode 01 & 02".
    private const string SpelledOut =
        @".*(\\|\/)(?<seriesname>[^\\\/]*?)[\s._-]*\bSeasons?[\s._-]*(?<seasonnumber>\d{1,4})[\s._-]*Episode[\s._-]*(?<epnumber>\d{1,4})(?:(?:\s*&\s*|\s+and\s+|-)(?<endingepnumber>\d{1,3})(?![0-9]))?[^\\\/]*$";

    // What is left of a name that was only an episode marker, such as "S01" or "1x".
    // A series called "24", "1923" or "911" is all digits, so a marker must carry its S, x or E.
    private static readonly Regex _bareMarker = new(@"^[\s._\-\[\(]*(?:s[0-9]{1,4}(?:e[0-9]{0,4})?|[0-9]{1,4}x[0-9]{0,4})[\s._\-\]\)]*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Group and source tags a release puts in front of the title, as in "[BDMux - AC3 ITA-ENG] Heroes".
    private static readonly Regex _leadingGroups = new(@"^(?:\s*\[[^\]]*\]\s*)+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // A folder that only names a season: "Season 06 (BD)", "Series 20", "S01", "1 сезон".
    private static readonly Regex _seasonFolder = new(@"^(?:(?:seasons?|saison|temporada|stagione|staffel|series|s)\s*[0-9]{1,4}(?![0-9])|[0-9]{1,4}\s*(?:сезон|season))", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A folder named after the rip rather than the show, as in "DVDRip.MKV.720x480".
    private static readonly Regex _ripFolder = new(@"^(?:dvdrip|bdrip|brrip|webrip|web-dl|hdtv|bluray|remux|480p|576p|720p|1080p|2160p|x264|x265|extras?|subs?|featurettes?)(?![a-z])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A name that opens with its episode marker, as in "4x02 - Luther Gillis" or "(7x1) New".
    private static readonly Regex _startsWithMarker = new(@"^[\s._\-\[\(]*(?:s?[0-9]{1,4}x[0-9]{1,4}(?:-[0-9]{1,4})?|s[0-9]{1,4}e[0-9]{1,4})[\s._\-\]\)]*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex _spaces = new(@"\s{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // What cannot end a series folder's name: bracketed year and number ranges, and a bracket opened and never
    // closed, as in "MASH 4077th [All" or "TV - Arthur (01-10)".
    private static readonly Regex _seriesNoise = new(@"\s*[\(\[]\s*[0-9]{1,4}(?:\s*[-–]\s*[0-9]{1,4})?\s*[\)\]]|\s*[\(\[][^\)\]]*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Rip words the film tag list does not know, which only ever trail a series name.
    private static readonly Regex _ripTail = new(@"[\s._\-]+(?:webrip|web-?dlrip|dvdremux|remux|iptvrip|satrip|tvrip|hdrip|dvd9|dvd5)(?![a-z]).*$", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "Season 3 Angel": a season named in front of the series.
    private static readonly Regex _leadingSeason = new(@"^(?:season|saison|temporada)\s*[0-9]{1,4}\s*[-._]*\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // A release's own season marker, which is where its series name ends.
    private static readonly Regex _releaseSeason = new(
        @"^(?<name>.+?)[\s._\-\[\(]+(?:s[0-9]{1,2}(?:[\s._-]*e[0-9]+)?|[0-9]{1,4}x[0-9]{1,4}|seasons?|saison|temporada|stagione|staffel|complete|completa|[0-9]{1,3}\s*(?:сезон|season))(?![a-z])",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] _videoExtensions =
    {
        ".mkv", ".mp4", ".avi", ".m4v", ".ts", ".mov", ".wmv", ".mpg", ".mpeg", ".flv", ".webm"
    };

    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // Tokens after which a release name is no longer the title.
    private const string ReleaseTags =
        "3d|sbs|tab|hsbs|htab|mvc|hdr|hdc|uhd|ultrahd|4k|ac3|dts|custom|dc|divx|divx5|dsr|dsrip|dutch|dvd|dvdrip|dvdscr|"
        + "dvdscreener|screener|dvdivx|cam|fragment|fs|hdtv|hdrip|hdtvrip|internal|limited|multi|subs|ntsc|ogg|ogm|pal|"
        + "pdtv|proper|repack|rerip|retail|cd[1-9]|r5|bd5|bd|se|svcd|swedish|german|read.nfo|nfofix|unrated|ws|web-dl|"
        + "telesync|ts|telecine|tc|brrip|bdrip|480p|480i|576p|576i|720p|720i|1080p|1080i|2160p|hrhd|hrhdtv|hddvd|bluray|"
        + "blu-ray|x264|x265|h264|h265|xvid|xvidvd|xxx|www.www|aac";

    // The year is the last 19xx/20xx that has a title in front of it and is not part of a longer number or a date.
    // A year right after punctuation wins over one after a space, so "1990-1994 2024" reads as 1994.
    private static readonly Regex[] _years =
    {
        new(@"^(?<name>.+[^_,.()\[\]\-])[_.()\[\]\-](?<year>(?:19|20)[0-9]{2})(?![0-9]|\W[0-9]{2}\W[0-9]{2})", Options),
        new(@"^(?<name>.+[^_,.()\[\]\-])[ _.()\[\]\-]+(?<year>(?:19|20)[0-9]{2})(?![0-9]|\W[0-9]{2}\W[0-9]{2})", Options),
    };

    // Applied in order and cumulatively, each result trimmed.
    private static readonly Regex[] _cleanStrings =
    {
        new(@"^\s*(?<cleaned>.+?)[ _,.()\[\]\-](?:" + ReleaseTags + @")(?=[ _,.()\[\]\-]|$)", Options),
        new(@"^\s*(?<cleaned>.+?)(?:\s*\[[^\]]+\]\s*)+(?:\.[^\s]+)?$", Options),
        new(@"^\s*(?<cleaned>.+?)\WE[0-9]+(?:-|~)E?[0-9]+(?:\W|$)", Options),
        new(@"^\s*\[[^\]]+\](?!\.\w+$)\s*(?<cleaned>.+)", Options),
        new(@"^\s*(?<cleaned>.+?)\s+-\s+[0-9]+\s*$", Options),
        new(@"^\s*(?<cleaned>.+?)(?:[-._ ](?:trailer|sample)|-(?:scene|clip|behindthescenes|deleted|deletedscene|featurette|short|interview|other|extra))$", Options),
    };

    private readonly NamingOptions _options = CreateNamingOptions();
    private readonly EpisodeResolver _episodes;

    /// <summary>Initializes a new instance of the <see cref="ReleaseNames"/> class.</summary>
    public ReleaseNames()
    {
        _episodes = new EpisodeResolver(_options);
    }

    /// <summary>Reports whether a path looks like a video file worth publishing.</summary>
    /// <param name="path">A path from a torrent's file list.</param>
    /// <returns><c>true</c> when the extension is one of the video extensions.</returns>
    public static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        return _videoExtensions.Any(candidate => string.Equals(extension, candidate, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads a file as an episode, when it is one.</summary>
    /// <param name="torrentName">The release name, which stands in for the containing folder.</param>
    /// <param name="filePath">The file's path within the torrent.</param>
    /// <returns>The episode, or <c>null</c> when the file is not one.</returns>
    /// <remarks>
    /// The series name comes from the containing folder, so the torrent name is synthesized as that folder. The
    /// optimistic expressions stay off: they read a release year as a season and episode pair.
    /// </remarks>
    public EpisodeName? ParseEpisode(string torrentName, string filePath)
    {
        ArgumentNullException.ThrowIfNull(torrentName);
        ArgumentNullException.ThrowIfNull(filePath);

        var path = Synthesize(torrentName, filePath);
        var parsed = _episodes.Resolve(path, false, isOptimistic: false);

        if (parsed?.SeasonNumber is not int season || parsed.EpisodeNumber is not int episode)
        {
            return null;
        }

        // Upstream fills a missing series name from any named expression, the optimistic ones included, which
        // read "Cleopatra 2525" as season 25 and "Espacial_3000" as season 30. Only the others are trusted here.
        // A file named only "1x01 - Title" or "S01E19-..." carries no series at all; the release it came in does.
        var series = SeriesFromFileName(path) ?? SeriesFromFolders(torrentName, filePath);

        var ending = parsed.EndingEpisodeNumber > episode ? parsed.EndingEpisodeNumber : null;
        return string.IsNullOrWhiteSpace(series) ? null : new EpisodeName(series, season, episode, ending);
    }

    /// <summary>Splits a humanised name into a title and a year, the way a media server searches for it.</summary>
    /// <param name="name">A humanised release or series name.</param>
    /// <returns>The title and, when the name carries one, the year.</returns>
    /// <remarks>
    /// Held to what the Jellyfin plugins produced (<c>parse-golden.tsv.gz</c>), because the result names an Emby
    /// folder and a folder is an item's identity: everything after the year goes, then release tags, bracketed
    /// groups, episode ranges and extra suffixes are cut in turn.
    /// </remarks>
    public static (string Name, int? Year) ParseName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var title = name;
        int? year = null;
        foreach (var expression in _years)
        {
            var match = expression.Match(name);
            if (match.Success)
            {
                title = match.Groups["name"].Value.TrimEnd();
                year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
                break;
            }
        }

        return (CleanString(title), year);
    }

    private static string CleanString(string name)
    {
        var cleaned = name;
        var changed = false;
        foreach (var expression in _cleanStrings)
        {
            var match = expression.Match(cleaned);
            if (match.Success)
            {
                cleaned = match.Groups["cleaned"].Value.Trim();
                changed = true;
            }
        }

        return changed ? cleaned : name;
    }

    /// <summary>The name a series is filed under.</summary>
    /// <param name="episode">A parsed episode.</param>
    /// <returns>The series title without release tags, a leading group tag or a year.</returns>
    public static string SeriesTitle(EpisodeName episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        var raw = _leadingSeason.Replace(_leadingGroups.Replace(Humanise(episode.SeriesName), string.Empty), string.Empty);
        var parsed = Tidy(ParseName(raw).Name);
        return parsed.Length > 0 ? parsed : Tidy(raw);
    }

    private static string Tidy(string name)
    {
        var tidy = _ripTail.Replace(_seriesNoise.Replace(name, string.Empty), string.Empty).Trim().TrimEnd('-', ',', ';', ':').Trim();
        return _spaces.Replace(tidy, " ");
    }

    /// <summary>The title a film is searched for under, from its file name or, failing that, its release.</summary>
    /// <param name="filePath">The file's path within the torrent.</param>
    /// <param name="torrentName">The release name.</param>
    /// <returns>The title and, when the name carries one, the year.</returns>
    public static (string Name, int? Year) MovieTitle(string filePath, string torrentName)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(torrentName);
        var parsed = ParseName(Humanise(Path.GetFileNameWithoutExtension(filePath)));
        return string.IsNullOrWhiteSpace(parsed.Name) ? (Humanise(torrentName), parsed.Year) : parsed;
    }

    /// <summary>Turns a dotted release name into something a metadata provider can search for.</summary>
    /// <param name="value">A release name or series name.</param>
    /// <returns>The name with separators turned back into spaces.</returns>
    public static string Humanise(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace('.', ' ').Replace('_', ' ').Trim();
    }

    internal static NamingOptions CreateNamingOptions()
    {
        var options = new NamingOptions();

        var episodes = options.EpisodeExpressions.ToList();
        var removed = episodes.RemoveAll(e => string.Equals(e.Expression, BareRange, StringComparison.Ordinal));
        var episodeGuards = episodes.Count(Guard);
        var multipleGuards = options.MultipleEpisodeExpressions.Count(Guard);

        if (removed != ExpectedRangesRemoved || episodeGuards != ExpectedEpisodeGuards || multipleGuards != ExpectedMultipleEpisodeGuards)
        {
            throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "The episode expressions changed underneath the plugin's edits: removed {0}, guarded {1} and {2}.",
                removed,
                episodeGuards,
                multipleGuards));
        }

        // Right after the SxxEyy expression, ahead of anything that could read "Season 1" as a number alone.
        episodes.Insert(1, new EpisodeExpression(SpelledOut) { IsNamed = true });
        options.EpisodeExpressions = episodes.ToArray();
        return options;
    }

    // The channel decimal guard: "DTS-HD.MA.5.1x265" is season 1 episode 265 to every NxNN expression.
    private static bool Guard(EpisodeExpression expression)
    {
        var guarded = expression.Expression
            .Replace(UnnamedSeason, @"[\/\._ \[\(-]" + NotChannelDecimal + "([0-9]+)x", StringComparison.Ordinal)
            .Replace(NamedSeason, NotChannelDecimal + NamedSeason, StringComparison.Ordinal);

        if (string.Equals(guarded, expression.Expression, StringComparison.Ordinal))
        {
            return false;
        }

        expression.Expression = guarded;
        return true;
    }

    private string? SeriesFromFileName(string path)
    {
        foreach (var expression in _options.EpisodeExpressions)
        {
            if (!expression.IsNamed || expression.IsOptimistic)
            {
                continue;
            }

            var match = expression.Regex.Match(path);
            var series = match.Success ? match.Groups["seriesname"].Value.Trim().Trim('_', '.', '-').Trim() : string.Empty;
            if (series.Trim('[', ']', '(', ')', ' ').Any(char.IsLetterOrDigit) && !_bareMarker.IsMatch(series))
            {
                return series;
            }
        }

        return null;
    }

    // The folders a file sits in, innermost first, then the release: the nearest one that names something other
    // than a season, an episode or a rip is the series.
    private static string SeriesFromFolders(string torrentName, string filePath)
    {
        var folders = filePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).SkipLast(1).Reverse().Append(torrentName);

        foreach (var folder in folders)
        {
            var title = _leadingGroups.Replace(Humanise(folder), string.Empty).Trim();
            if (_seasonFolder.IsMatch(title) || _ripFolder.IsMatch(title))
            {
                continue;
            }

            var match = _releaseSeason.Match(title);
            if (match.Success)
            {
                return match.Groups["name"].Value;
            }

            if (!_startsWithMarker.IsMatch(title) && title.Any(char.IsLetterOrDigit))
            {
                return title;
            }
        }

        // Nothing but markers: what is left of the release once they are gone.
        return _startsWithMarker.Replace(Humanise(torrentName), string.Empty).Trim();
    }

    private static string Synthesize(string torrentName, string filePath)
        => string.Format(CultureInfo.InvariantCulture, "/{0}/{1}", torrentName.Replace('/', '_'), Path.GetFileName(filePath));
}
