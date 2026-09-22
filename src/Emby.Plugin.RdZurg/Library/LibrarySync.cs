using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.RealDebrid;
using Emby.Plugin.RdZurg.Streaming;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>What one pass over the account did.</summary>
public sealed class SyncResult
{
    /// <summary>Gets or sets how many torrents the account listed.</summary>
    public int TorrentsSeen { get; set; }

    /// <summary>Gets or sets how many were already accounted for, and cost no detail call.</summary>
    public int TorrentsAlreadyKnown { get; set; }

    /// <summary>Gets or sets how many detail calls the pass spent.</summary>
    public int DetailCalls { get; set; }

    /// <summary>Gets or sets how many files were published for the first time.</summary>
    public int FilesWritten { get; set; }

    /// <summary>Gets or sets how many files had their URL rewritten.</summary>
    public int FilesRewritten { get; set; }

    /// <summary>Gets or sets how many files were already correct.</summary>
    public int FilesUnchanged { get; set; }

    /// <summary>Gets or sets how many files were removed.</summary>
    public int FilesDeleted { get; set; }

    /// <summary>Gets or sets how many releases were skipped as copies of something already published.</summary>
    public int Duplicates { get; set; }

    /// <summary>Gets or sets how many releases were held back by the version cap.</summary>
    public int Capped { get; set; }

    /// <summary>Gets or sets how many paths had to be made unique.</summary>
    public int Collisions { get; set; }

    /// <summary>Gets or sets whether cleanup was refused because too much would have gone at once.</summary>
    public bool CleanupRefused { get; set; }

    /// <summary>Gets or sets the account the pass published for.</summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Describes the pass for the log.</summary>
    /// <returns>One line.</returns>
    public override string ToString() => string.Format(
        CultureInfo.InvariantCulture,
        "{0} torrents ({1} already known, {2} detail calls): {3} written, {4} rewritten, {5} unchanged, {6} deleted, {7} duplicates, {8} capped{9}",
        TorrentsSeen,
        TorrentsAlreadyKnown,
        DetailCalls,
        FilesWritten,
        FilesRewritten,
        FilesUnchanged,
        FilesDeleted,
        Duplicates,
        Capped,
        CleanupRefused ? ", cleanup refused" : string.Empty);
}

/// <summary>
/// Turns a Real-Debrid account into the tree of <c>.strm</c> files Emby scans.
/// </summary>
public sealed class LibrarySync
{
    private readonly RealDebridClient _client;
    private readonly StrmTree _tree;
    private readonly Ledger _ledger;
    private readonly ILogger _logger;
    private readonly ReleaseNames _names = new();

    /// <summary>Initializes a new instance of the <see cref="LibrarySync"/> class.</summary>
    /// <param name="client">The provider client.</param>
    /// <param name="tree">The tree to write.</param>
    /// <param name="ledger">What is published where.</param>
    /// <param name="logger">Logger.</param>
    public LibrarySync(RealDebridClient client, StrmTree tree, Ledger ledger, ILogger logger)
    {
        _client = client;
        _tree = tree;
        _ledger = ledger;
        _logger = logger;
    }

    /// <summary>Runs one pass.</summary>
    /// <param name="options">The current settings.</param>
    /// <param name="baseUrl">Where Emby reaches its own stream route.</param>
    /// <param name="accountId">The Real-Debrid account id the URLs are signed for.</param>
    /// <param name="progress">Progress, 0 to 100.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>What the pass did.</returns>
    public async Task<SyncResult> RunAsync(
        PluginOptions options,
        string baseUrl,
        string accountId,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(accountId);

        var result = new SyncResult { AccountId = accountId };
        var torrents = await _client.GetTorrentsAsync(options.MaxTorrents, cancellationToken).ConfigureAwait(false);
        result.TorrentsSeen = torrents.Count;
        _logger.Info("RD zurg: Real-Debrid listed {0} torrents", torrents.Count);

        // Liveness comes from the complete listing, before any detail call can fail. Links of torrents that are
        // still downloading or temporarily errored count too: their files have not gone anywhere.
        var live = new HashSet<string>(torrents.SelectMany(t => t.Links).Select(RealDebridClient.LinkKey), StringComparer.Ordinal);

        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // Only a complete listing proves that a torrent is gone.
        var mayDelete = options.RemoveVanishedItems && options.MaxTorrents == 0;

        // Everything already published and still live keeps its path; only the URL is rebuilt, so a changed
        // signing key or account rewrites the file and nothing moves. A pass that may not delete keeps the rest
        // too: those files stay on disk whatever this pass decides, so they still fill their folder's versions and
        // still stand for their release.
        var pinned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _ledger.Published.Where(e => mayDelete ? live.Contains(e.Key) : e.Path.Length > 0).ToList())
        {
            desired[entry.Path] = Url(baseUrl, options, accountId, entry.Key, entry.File);
            owners[entry.Path] = entry.Key;
            sizes[entry.Path] = entry.Bytes;
            if (!mayDelete)
            {
                pinned.Add(entry.Path);
            }
        }

        var fingerprints = new HashSet<string>(
            _ledger.Published.Where(e => desired.ContainsKey(e.Path)).Select(e => e.Fingerprint),
            StringComparer.OrdinalIgnoreCase);

        // A release the version cap held back stays a candidate for as long as it is live, at the path it would
        // have had; the cap decides again below, so when one of the kept versions goes the largest of these takes
        // its place without its torrent being read again.
        foreach (var entry in _ledger.Entries.Where(e => e.Outcome == LedgerOutcome.Capped && e.Path.Length > 0 && live.Contains(e.Key)).ToList())
        {
            if (owners.TryAdd(entry.Path, entry.Key))
            {
                desired[entry.Path] = Url(baseUrl, options, accountId, entry.Key, entry.File);
                sizes[entry.Path] = entry.Bytes;
            }
        }

        // Whether a decision still stands. A copy was held back for a release that was published; once that one is
        // gone the copy's torrent is read again and the copy takes its place.
        bool Placed(LedgerEntry entry) => entry.Outcome switch
        {
            LedgerOutcome.Published => entry.Bytes > 0,
            LedgerOutcome.Duplicate => fingerprints.Contains(entry.Fingerprint),
            LedgerOutcome.Capped => true,
            _ => false,
        };

        bool IsPlaced(string key) => _ledger.Find(key) is { } entry && Placed(entry);

        // Collected first, decided second: a ledger rebuilt from the tree knows no sizes, and sizes are what
        // recognise a re-added release, so every detail this pass sees has to be in hand before anything is
        // called a duplicate.
        var details = new List<RdTorrentInfo>();
        var index = 0;
        foreach (var torrent in torrents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(++index * 70.0 / Math.Max(torrents.Count, 1));

            if (!string.Equals(torrent.Status, "downloaded", StringComparison.OrdinalIgnoreCase) || torrent.Links.Count == 0)
            {
                continue;
            }

            // The listing already carries the links, so recognising a torrent costs nothing. Links the parser
            // had no place for are remembered as well, or every pass would re-query the same torrents.
            if (_ledger.IsSettled(torrent.Id, torrent.Links.Select(RealDebridClient.LinkKey).ToList(), Placed))
            {
                result.TorrentsAlreadyKnown++;
                continue;
            }

            var info = await _client.GetTorrentInfoAsync(torrent.Id, cancellationToken).ConfigureAwait(false);
            result.DetailCalls++;

            if (info is not null)
            {
                details.Add(info);
            }
        }

        progress?.Report(75);

        var pairs = new List<(RdTorrentInfo Info, List<(RdFile File, string Key)> Files)>();
        foreach (var info in details)
        {
            var selected = info.Files.Where(f => f.Selected == 1).ToList();
            if (selected.Count == 0 || selected.Count != info.Links.Count)
            {
                // A torrent whose file list and links disagree is left alone and asked about again next pass.
                _logger.Debug(
                    "RD zurg: skipping {0}: {1} selected files against {2} links",
                    info.Filename,
                    selected.Count,
                    info.Links.Count);
                continue;
            }

            pairs.Add((info, selected.Select((file, i) => (File: file, Key: RealDebridClient.LinkKey(info.Links[i]))).ToList()));
        }

        // What is already published only needs its size filled in; its path never moves.
        foreach (var (info, files) in pairs)
        {
            foreach (var (file, key) in files)
            {
                if (_ledger.Find(key) is not { Outcome: LedgerOutcome.Published, Bytes: 0 } entry)
                {
                    continue;
                }

                entry.File = file.Path;
                entry.Bytes = file.Bytes;
                entry.TorrentId = info.Id;
                _ledger.Put(entry);
                desired[entry.Path] = Url(baseUrl, options, accountId, key, entry.File);
                owners[entry.Path] = key;
                sizes[entry.Path] = entry.Bytes;
                fingerprints.Add(entry.Fingerprint);
            }
        }

        // One spelling per folder. Two releases of one title can differ only in case - the test account holds "One
        // More Shot" and "One more shot" - and Emby groups a folder's files as versions of one item only when each
        // file starts with the folder's name. Left alone that is two items on a case-sensitive filesystem and a
        // folder whose files disagree with it on every other. What is already published keeps its spelling, since
        // its path never moves, so it is what a newcomer joins.
        var movieFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seriesFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in desired.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            var parts = path.Split('/');
            if (parts.Length > 2)
            {
                (parts[0] == StrmPaths.Movies ? movieFolders : seriesFolders).TryAdd(parts[1], parts[1]);
            }
        }

        foreach (var (info, files) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var (file, key) in files.Where(p => !ReleaseNames.IsVideo(p.File.Path)))
            {
                Ignore(info.Id, file, key);
            }

            var videos = files
                .Where(p => ReleaseNames.IsVideo(p.File.Path))
                .OrderByDescending(p => p.File.Bytes)
                .ToList();

            if (videos.Count == 0)
            {
                continue;
            }

            // A season pack is one torrent holding many episodes, so every video file is offered to the episode
            // parser. Only when none of them is an episode does the biggest file become a film.
            var episodes = videos
                .Select(v => (v.File, v.Key, Episode: _names.ParseEpisode(info.Filename, v.File.Path)))
                .Where(v => v.Episode is not null)
                .ToList();

            if (episodes.Count > 0)
            {
                // A pack's featurettes and samples have no place in it, and are remembered so the pack is not read
                // again next pass.
                foreach (var (file, key) in videos.Where(v => !episodes.Any(e => string.Equals(e.Key, v.Key, StringComparison.Ordinal))))
                {
                    Ignore(info.Id, file, key);
                }

                foreach (var (file, key, episode) in episodes)
                {
                    if (IsPlaced(key))
                    {
                        continue;
                    }

                    // The same name and size under another key is a copy, for an episode as for a film: Real-Debrid
                    // gives a re-added release fresh link keys (205 of the test account's 234 copies), and each
                    // copy would otherwise become one more version of the same episode.
                    if (!fingerprints.Add(Fingerprints.Of(file.Path, file.Bytes)))
                    {
                        _ledger.Put(new LedgerEntry { Key = key, TorrentId = info.Id, File = file.Path, Bytes = file.Bytes, Outcome = LedgerOutcome.Duplicate });
                        result.Duplicates++;
                        continue;
                    }

                    var label = StrmPaths.Label(file.Path, key);
                    var series = Canonical(seriesFolders, StrmPaths.Component(ReleaseNames.SeriesTitle(episode!), key));
                    var path = Claim(
                        StrmPaths.EpisodePath(series, episode!.SeasonNumber, episode.EpisodeNumber, episode.EndingEpisodeNumber, label, key),
                        key,
                        owners,
                        () => StrmPaths.EpisodePath(series, episode.SeasonNumber, episode.EpisodeNumber, episode.EndingEpisodeNumber, label + " [" + key + "]", key),
                        result);

                    Publish(desired, owners, sizes, path, key, info.Id, file.Path, file.Bytes, baseUrl, options, accountId);
                }

                continue;
            }

            // Only the biggest video is the film; the rest (featurettes, a sample, a collection's other films) are
            // remembered so the torrent is not read again next pass.
            var primary = videos[0];
            foreach (var (file, key) in videos.Skip(1))
            {
                Ignore(info.Id, file, key);
            }

            if (IsPlaced(primary.Key))
            {
                continue;
            }

            // A copy of a release is the same name and size. Real-Debrid used to hand identical content the same
            // link key, which the key check above catches; a re-added release now mostly gets fresh keys.
            var fingerprint = Fingerprints.Of(primary.File.Path, primary.File.Bytes);
            if (!fingerprints.Add(fingerprint))
            {
                _ledger.Put(new LedgerEntry
                {
                    Key = primary.Key,
                    TorrentId = info.Id,
                    File = primary.File.Path,
                    Bytes = primary.File.Bytes,
                    Outcome = LedgerOutcome.Duplicate,
                });
                result.Duplicates++;
                continue;
            }

            var (title, year) = ReleaseNames.MovieTitle(primary.File.Path, info.Filename);
            var folder = Canonical(movieFolders, StrmPaths.MovieFolder(title, year, primary.Key));
            var movieLabel = StrmPaths.Label(primary.File.Path, primary.Key);
            var moviePath = Claim(
                StrmPaths.MoviePath(folder, movieLabel),
                primary.Key,
                owners,
                () => StrmPaths.MoviePath(folder, movieLabel + " [" + primary.Key + "]"),
                result);

            Publish(desired, owners, sizes, moviePath, primary.Key, info.Id, primary.File.Path, primary.File.Bytes, baseUrl, options, accountId);
        }

        progress?.Report(88);
        result.Capped = Cap(desired, owners, sizes, pinned);

        progress?.Report(92);

        var changes = _tree.Apply(desired, mayDelete, options.AllowLargeCleanup);
        result.FilesWritten = changes.Written;
        result.FilesRewritten = changes.Rewritten;
        result.FilesUnchanged = changes.Unchanged;
        result.FilesDeleted = changes.Deleted;
        result.CleanupRefused = changes.CleanupRefused;

        if (mayDelete && !changes.CleanupRefused)
        {
            // A complete listing says which links are gone, whatever became of them.
            foreach (var entry in _ledger.Entries.Where(e => !live.Contains(e.Key)).ToList())
            {
                _ledger.Remove(entry.Key);
            }
        }

        progress?.Report(100);
        return result;
    }

    /// <summary>Rebuilds the ledger from the tree when its own file is gone.</summary>
    /// <returns>How many entries were recovered.</returns>
    public int RecoverLedger() => _ledger.RebuildFromTree(_tree.Root, StreamUrls.KeyOf, StreamUrls.FileNameOf);

    private void Ignore(string torrentId, RdFile file, string key)
    {
        if (!_ledger.Knows(key))
        {
            _ledger.Put(new LedgerEntry { Key = key, TorrentId = torrentId, File = file.Path, Bytes = file.Bytes, Outcome = LedgerOutcome.Ignored });
        }
    }

    private static string Url(string baseUrl, PluginOptions options, string accountId, string key, string fileName)
        => LinkResolver.BuildUrl(baseUrl, key, fileName, options.StreamSecret, accountId);

    /// <summary>Keeps one spelling per folder: whichever is already published, or else whichever this pass met first.</summary>
    /// <param name="folders">The spellings already chosen.</param>
    /// <param name="name">The folder name this release parsed to.</param>
    /// <returns>The spelling to publish under.</returns>
    private static string Canonical(Dictionary<string, string> folders, string name)
    {
        if (folders.TryGetValue(name, out var chosen))
        {
            return chosen;
        }

        folders[name] = name;
        return name;
    }

    private static string Claim(string path, string key, Dictionary<string, string> owners, Func<string> alternative, SyncResult result)
    {
        if (!owners.TryGetValue(path, out var owner) || string.Equals(owner, key, StringComparison.Ordinal))
        {
            return path;
        }

        result.Collisions++;
        return alternative();
    }

    private void Publish(
        Dictionary<string, string> desired,
        Dictionary<string, string> owners,
        Dictionary<string, long> sizes,
        string path,
        string key,
        string torrentId,
        string file,
        long bytes,
        string baseUrl,
        PluginOptions options,
        string accountId)
    {
        // A path already spoken for by another link, even after the alternative, is left to its owner.
        if (owners.TryGetValue(path, out var owner) && !string.Equals(owner, key, StringComparison.Ordinal))
        {
            _logger.Warn("RD zurg: {0} is already published at {1}; skipping {2}", owner, path, key);
            _ledger.Put(new LedgerEntry { Key = key, TorrentId = torrentId, File = file, Bytes = bytes, Outcome = LedgerOutcome.Duplicate });
            return;
        }

        desired[path] = Url(baseUrl, options, accountId, key, file);
        owners[path] = key;
        sizes[path] = bytes;
        _ledger.Put(new LedgerEntry
        {
            Key = key,
            Path = path,
            TorrentId = torrentId,
            File = file,
            Bytes = bytes,
            Outcome = LedgerOutcome.Published,
        });
    }

    /// <summary>
    /// Keeps a film folder within the number of versions Emby will group.
    /// </summary>
    /// <remarks>
    /// Past the cap Emby stops grouping altogether and shows every file as its own film; the account measured for
    /// this had 109 releases of The Matrix. The largest are kept, which is also what a viewer would pick.
    /// </remarks>
    private int Cap(Dictionary<string, string> desired, Dictionary<string, string> owners, Dictionary<string, long> sizes, HashSet<string> pinned)
    {
        var capped = 0;
        var folders = desired.Keys
            .Where(p => p.StartsWith(StrmPaths.Movies + "/", StringComparison.OrdinalIgnoreCase))
            .GroupBy(p => p[..p.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var folder in folders)
        {
            // A file this pass may not delete keeps its place, or the folder would end up holding more than the cap.
            var ranked = folder
                .OrderByDescending(pinned.Contains)
                .ThenByDescending(p => sizes.TryGetValue(p, out var bytes) ? bytes : 0)
                .ThenBy(p => p, StringComparer.Ordinal)
                .ToList();

            // A release the cap held back before and now fits is published again.
            foreach (var entry in ranked.Take(StrmPaths.MaxVersions).Select(_ledger.AtPath).Where(e => e is { Outcome: LedgerOutcome.Capped }))
            {
                entry!.Outcome = LedgerOutcome.Published;
                _ledger.Put(entry);
            }

            var newlyCapped = 0;
            foreach (var path in ranked.Skip(StrmPaths.MaxVersions))
            {
                var entry = _ledger.AtPath(path);
                desired.Remove(path);
                owners.Remove(path);
                if (entry is not null && entry.Outcome != LedgerOutcome.Capped)
                {
                    entry.Outcome = LedgerOutcome.Capped;
                    _ledger.Put(entry);
                    newlyCapped++;
                }

                capped++;
            }

            if (newlyCapped > 0)
            {
                _logger.Info("RD zurg: {0} holds more releases than Emby groups; keeping the {1} largest", folder.Key, StrmPaths.MaxVersions);
            }
        }

        return capped;
    }
}
