// Golden title/year and series-name output of the Jellyfin 12 plugin's parsing, used as the
// reference the Emby port's vendored parser is held to.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Emby.Naming.Common;
using Emby.Naming.Video;
using Jellyfin.Plugin.RdZurg.Library;

var fixtures = args[0];
var options = new NamingOptions();
(string Name, int? Year) ParseName(string name)
{
    // Jellyfin 12 LibraryManager.ParseName: CleanDateTime, then TryCleanString on the result.
    var dated = VideoResolver.CleanDateTime(name, options);
    var cleaned = VideoResolver.TryCleanString(dated.Name, options, out var newName) ? newName : dated.Name;
    return (cleaned, dated.Year);
}
var names = new ReleaseNames();
var rows = new List<string>();
void Movie(string source, string release, string file)
{
    var (n, y) = ParseName(ReleaseNames.Humanise(Path.GetFileNameWithoutExtension(file)));
    rows.Add(string.Join('\t', "movie", source, release, file, n, y?.ToString() ?? "-"));
}
using (var reader = new StreamReader(new GZipStream(File.OpenRead(Path.Combine(fixtures, "episode-corpus.tsv.gz")), CompressionMode.Decompress), Encoding.UTF8))
{
    string? line;
    while ((line = reader.ReadLine()) is not null)
    {
        var p = line.Split('\t');
        if (p.Length != 3) continue;
        if (p[2] == "-") { Movie("corpus", p[0], p[1]); continue; }
        var ep = names.ParseEpisode(p[0], p[1]);
        if (ep is null) continue;
        var raw = ReleaseNames.Humanise(ep.SeriesName!);
        var parsed = ParseName(raw).Name;
        var series = string.IsNullOrWhiteSpace(parsed) ? raw : parsed;
        rows.Add(string.Join('\t', "series", "corpus", p[0], p[1], series, $"S{ep.SeasonNumber}E{ep.EpisodeNumber}" + (ep.EndingEpisodeNumber is int end ? $"-E{end}" : "")));
    }
}
foreach (var fixture in new[] { "rd-library-1.0.2.0.json", "rd-misfiled-episodes-1.0.4.0.json" })
{
    using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, fixture)));
    if (!doc.RootElement.TryGetProperty("info", out var info)) continue;
    foreach (var t in info.EnumerateObject())
    {
        var release = t.Value.GetProperty("filename").GetString()!;
        foreach (var f in t.Value.GetProperty("files").EnumerateArray())
        {
            var path = f.GetProperty("path").GetString()!;
            if (f.GetProperty("selected").GetInt32() != 1 || !ReleaseNames.IsVideo(path)) continue;
            var ep = names.ParseEpisode(release, path);
            if (ep is null) { Movie(fixture, release, path); continue; }
            var raw = ReleaseNames.Humanise(ep.SeriesName!);
            var parsed = ParseName(raw).Name;
            rows.Add(string.Join('\t', "series", fixture, release, path, string.IsNullOrWhiteSpace(parsed) ? raw : parsed, $"S{ep.SeasonNumber}E{ep.EpisodeNumber}" + (ep.EndingEpisodeNumber is int end ? $"-E{end}" : "")));
        }
    }
}
var outPath = args[1];
using (var gz = new StreamWriter(new GZipStream(File.Create(outPath), CompressionLevel.SmallestSize), new UTF8Encoding(false)))
{
    gz.WriteLine("# kind\tsource\trelease\tfile\tname\tyear-or-episode  (generated from rd-zurg-for-jellyfin ReleaseNames + Jellyfin.Naming 12.0.0 ParseName)");
    foreach (var r in rows) gz.WriteLine(r);
}
Console.WriteLine($"{rows.Count} rows: {rows.Count(r => r.StartsWith("movie"))} movie, {rows.Count(r => r.StartsWith("series"))} series");
