// Derived from MediaBrowser/Emby.Naming (commit c2a5776, 2019-05-16), MIT licensed; see LICENSE-Emby.Naming.md.
// Kept close to upstream on purpose. The plugin's own changes to the expressions live in Library/ReleaseNames.cs.
namespace Emby.Plugin.RdZurg.Naming;

internal sealed class EpisodePathParserResult
{
    public int? SeasonNumber { get; set; }

    public int? EpisodeNumber { get; set; }

    public int? EndingEpisodeNumber { get; set; }

    public string? SeriesName { get; set; }

    public bool Success { get; set; }

    public bool IsByDate { get; set; }

    public int? Year { get; set; }

    public int? Month { get; set; }

    public int? Day { get; set; }
}
