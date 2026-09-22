using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Emby.Plugin.RdZurg.Library;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

/// <summary>
/// A year followed by an <c>x</c> and a number is a frame size only when that number is a frame's height.
/// </summary>
/// <remarks>
/// <c>resolution-corpus.tsv.gz</c> is every video file in DMM's RD and AllDebrid availability tables whose release or
/// file name carries a four-digit number from 1900 to 2099, an <c>x</c> and three or four digits, drawn on 2026-09-22:
/// 32,224 release and file pairs, shared with the Jellyfin plugins. The lowest height behind a year-shaped width in it
/// is 696 (<c>1920x696</c>); everything below 600 is something else, a codec glued to a real year in
/// <c>Rang De Basanti 2006x264</c> or a season and episode in <c>Formula.1.2025x126</c>.
/// </remarks>
public class ResolutionFloorTests
{
    // A year-shaped number, an x and three or four digits, as a name carries it.
    private static readonly Regex _yearByNumber = new(@"(?<![0-9])(?<year>(?:19|20)[0-9]{2})[xX](?<height>[0-9]{3,4})(?![0-9])", RegexOptions.CultureInvariant);

    private readonly ReleaseNames _names = new();

    /// <summary>Real names whose year sits against its codec; the year is the film's.</summary>
    [Theory]
    [InlineData("Rang De Basanti 2006x264 720p Esub BluRay Dual Audio English Hindi GOPISAHI", "Rang De Basanti", 2006)]
    [InlineData("Mini-Skirt Gang[1974x264.DVDrip(ShawBros)", "Mini-Skirt Gang", 1974)]
    [InlineData("Лоракс.2012x264.BDRip.(1080p)", "Лоракс", 2012)]
    public void KeepsAYearGluedToItsCodec(string release, string name, int year)
    {
        Assert.Equal((name, (int?)year), ReleaseNames.MovieTitle("/" + release + ".mkv", release));
    }

    /// <summary>
    /// Real Formula 1 releases numbered by year and round. The episode reads as it did, and the name read as a
    /// title keeps 2025 as its year.
    /// </summary>
    [Theory]
    [InlineData("Formula.1.2025x126.R24.AbuDhabiGP.Race.MULTi.1080p.SS", 126)]
    [InlineData("Formula.1.2025x101xR19.UnitedStatesGP.Race.MULTi.720p.SS", 101)]
    public void ReadsARoundNumberedByYear(string release, int episode)
    {
        var parsed = _names.ParseEpisode(release, "/" + release + ".mkv");

        Assert.NotNull(parsed);
        Assert.Equal((2025, episode), (parsed!.SeasonNumber, parsed.EpisodeNumber));
        Assert.Equal("Formula 1", ReleaseNames.SeriesTitle(parsed));
        Assert.Equal(("Formula 1", (int?)2025), ReleaseNames.ParseName(ReleaseNames.Humanise(release)));
    }

    /// <summary>Real frame sizes, from 1920x696 up, are still never a year.</summary>
    [Theory]
    [InlineData("2015.The.Hateful.Eight.1920x696.BDRip.x264.DTS-HD.MA", "2015 The Hateful Eight 1920x696", null)]
    [InlineData("1989 - Povolení zabíjet (r.1989 - 2048x872)", "1989 - Povolení zabíjet (r", 1989)]
    [InlineData("[Arid] Samurai Champloo [Dual-Audio][BDRip 1920x1080 HEVC FLAC]", "Samurai Champloo", null)]
    [InlineData("Forbidden.Zone.1980.1920x1080.BDRip.x264.DTS-HD.MA.Eng", "Forbidden Zone", 1980)]
    [InlineData("Blade.Runner.2049.2017.3840x2160", "Blade Runner 2049", 2017)]
    public void StillRefusesAFrameWidth(string release, string name, int? year)
    {
        Assert.Equal((name, year), ReleaseNames.ParseName(ReleaseNames.Humanise(release)));
    }

    /// <summary>
    /// Every corpus name with a year-shaped number in front of an <c>x</c> and a number below 600 reads a year:
    /// that number's own or one later in the name.
    /// </summary>
    [Fact]
    public void KeepsEveryYearBelowTheFloor()
    {
        var names = Names().Where(n => _yearByNumber.Matches(n).Any(m => Height(m) < 600)).ToList();
        var lost = new List<string>();

        foreach (var name in names)
        {
            var year = ReleaseNames.ParseName(ReleaseNames.Humanise(name)).Year;
            var first = _yearByNumber.Matches(name).First(m => Height(m) < 600);
            if (year is not int value || !name[first.Index..].Contains(value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                lost.Add(FormattableString.Invariant($"{year?.ToString(CultureInfo.InvariantCulture) ?? "-"}  <=  {name}"));
            }
        }

        Assert.True(names.Count >= 40, $"only {names.Count} names below the floor");
        Assert.True(lost.Count == 0, $"{lost.Count} of {names.Count} lost their year:{Environment.NewLine}{string.Join(Environment.NewLine, lost.Take(20))}");
    }

    /// <summary>No corpus name reads the width of a frame 600 or more high as its year.</summary>
    [Fact]
    public void NeverReadsAFrameWidthAsAYear()
    {
        var names = Names();
        var wrong = new List<string>();

        foreach (var name in names)
        {
            var year = ReleaseNames.ParseName(ReleaseNames.Humanise(name)).Year;
            if (year is int value && !Regex.IsMatch(
                    name,
                    FormattableString.Invariant($"(?<![0-9]){value}(?![0-9]|[xX](?:[6-9][0-9]{{2}}|[1-9][0-9]{{3}})(?![0-9]))")))
            {
                wrong.Add(FormattableString.Invariant($"{value}  <=  {name}"));
            }
        }

        Assert.True(names.Count > 28000, $"only {names.Count} names");
        Assert.True(wrong.Count == 0, $"{wrong.Count} read a frame width as a year:{Environment.NewLine}{string.Join(Environment.NewLine, wrong.Take(20))}");
    }

    private static int Height(Match match) => int.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture);

    private static List<string> Names()
        => Fixture.Tsv("resolution-corpus.tsv.gz")
            .SelectMany(r => new[] { r[0], Path.GetFileNameWithoutExtension(r[1]) })
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
