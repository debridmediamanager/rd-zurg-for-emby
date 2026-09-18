using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>What became of one Real-Debrid link.</summary>
public sealed class LedgerEntry
{
    /// <summary>Gets or sets the 13 character content key.</summary>
    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the file this link is published at, relative to the tree root.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the torrent the link came from.</summary>
    [JsonPropertyName("torrent")]
    public string TorrentId { get; set; } = string.Empty;

    /// <summary>Gets or sets the file's name inside the torrent.</summary>
    [JsonPropertyName("file")]
    public string File { get; set; } = string.Empty;

    /// <summary>Gets or sets the file's size, which together with the name recognises a re-add.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>Gets or sets what the sync did with the link.</summary>
    [JsonPropertyName("outcome")]
    public LedgerOutcome Outcome { get; set; }

    /// <summary>Gets the fingerprint that recognises the same release under another hash.</summary>
    [JsonIgnore]
    public string Fingerprint => Fingerprints.Of(File, Bytes);
}

/// <summary>What the sync decided about a link.</summary>
public enum LedgerOutcome
{
    /// <summary>The link has a file in the tree.</summary>
    Published,

    /// <summary>The same release is already published under another link.</summary>
    Duplicate,

    /// <summary>Not a video, or a file the parser had no place for.</summary>
    Ignored,
}

/// <summary>Fingerprints a release the way the Jellyfin plugin did.</summary>
public static class Fingerprints
{
    /// <summary>Builds the fingerprint of a file.</summary>
    /// <param name="fileName">The file's name inside the torrent.</param>
    /// <param name="bytes">Its size.</param>
    /// <returns>The fingerprint.</returns>
    public static string Of(string fileName, long bytes)
        => string.Format(CultureInfo.InvariantCulture, "{0}|{1}", System.IO.Path.GetFileName(fileName), bytes);
}

/// <summary>
/// Remembers which link is published at which file.
/// </summary>
/// <remarks>
/// In Emby a file's path is the item's identity: move it and the item is deleted and recreated, which drops
/// playlist membership and, for anything metadata could not match, the watch state. So a path, once published, is
/// never recomputed - the ledger, not the parser, decides where a known link lives. It also carries the sizes the
/// duplicate check needs, which a <c>.strm</c> file cannot hold.
/// </remarks>
public sealed class Ledger
{
    private readonly Dictionary<string, LedgerEntry> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _byPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets every entry.</summary>
    public IReadOnlyCollection<LedgerEntry> Entries => _byKey.Values;

    /// <summary>Gets the entries that have a file in the tree.</summary>
    public IEnumerable<LedgerEntry> Published => _byKey.Values.Where(e => e.Outcome == LedgerOutcome.Published);

    /// <summary>Reads the ledger, or starts an empty one.</summary>
    /// <param name="file">The ledger's path.</param>
    /// <returns>The ledger.</returns>
    public static Ledger Load(string file)
    {
        var ledger = new Ledger();
        if (!File.Exists(file))
        {
            return ledger;
        }

        var entries = JsonSerializer.Deserialize<List<LedgerEntry>>(File.ReadAllText(file)) ?? new List<LedgerEntry>();
        foreach (var entry in entries.Where(e => StreamAccessKey(e.Key)))
        {
            ledger.Put(entry);
        }

        return ledger;
    }

    /// <summary>Writes the ledger, replacing it only once the new copy is complete.</summary>
    /// <param name="file">The ledger's path.</param>
    public void Save(string file)
    {
        ArgumentException.ThrowIfNullOrEmpty(file);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_byKey.Values.OrderBy(e => e.Key, StringComparer.Ordinal), new JsonSerializerOptions { WriteIndented = false }));
        File.Move(temp, file, true);
    }

    /// <summary>Rebuilds what the tree itself can prove: every published file names its own link.</summary>
    /// <param name="root">The tree root.</param>
    /// <param name="keyOf">Reads the link key out of a file's contents.</param>
    /// <param name="fileOf">Reads the release's file name out of a file's contents.</param>
    /// <returns>How many entries were recovered.</returns>
    public int RebuildFromTree(string root, Func<string, string?> keyOf, Func<string, string?> fileOf)
    {
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(fileOf);
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var recovered = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.strm", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            var key = keyOf(content);
            if (key is null || _byKey.ContainsKey(key))
            {
                continue;
            }

            var relative = System.IO.Path.GetRelativePath(root, file).Replace('\\', '/');

            // The URL carries the release's own file name, so the rebuilt entry rebuilds to the same URL and
            // nothing is rewritten. Only the size is lost, and the next pass fills it in.
            Put(new LedgerEntry
            {
                Key = key,
                Path = relative,
                File = fileOf(content) ?? System.IO.Path.GetFileName(file),
                Outcome = LedgerOutcome.Published,
            });
            recovered++;
        }

        return recovered;
    }

    /// <summary>Looks a link up.</summary>
    /// <param name="key">The content key.</param>
    /// <returns>The entry, or <c>null</c>.</returns>
    public LedgerEntry? Find(string key) => _byKey.TryGetValue(key, out var entry) ? entry : null;

    /// <summary>Reports whether the sync has already decided what to do with a link.</summary>
    /// <param name="key">The content key.</param>
    /// <returns>Whether it is known.</returns>
    public bool Knows(string key) => _byKey.ContainsKey(key);

    /// <summary>
    /// Reports whether nothing more needs to be asked about a link.
    /// </summary>
    /// <param name="key">The content key.</param>
    /// <returns>Whether the entry is complete.</returns>
    /// <remarks>
    /// An entry rebuilt from the tree knows where its file is but not how big the release was, and the size is
    /// what recognises the same release re-added under another hash. Such an entry costs one more detail call.
    /// </remarks>
    public bool IsSettled(string key)
        => _byKey.TryGetValue(key, out var entry) && (entry.Outcome != LedgerOutcome.Published || entry.Bytes > 0);

    /// <summary>Finds what is published at a path.</summary>
    /// <param name="path">A path relative to the tree root.</param>
    /// <returns>The entry, or <c>null</c>.</returns>
    public LedgerEntry? AtPath(string path) => _byPath.TryGetValue(path, out var key) ? _byKey[key] : null;

    /// <summary>Reports whether a release with this fingerprint is already published.</summary>
    /// <param name="fingerprint">The fingerprint.</param>
    /// <returns>Whether it is a duplicate.</returns>
    public bool HasFingerprint(string fingerprint)
        => Published.Any(e => string.Equals(e.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

    /// <summary>Records an entry, replacing whatever the key had before.</summary>
    /// <param name="entry">The entry.</param>
    public void Put(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_byKey.TryGetValue(entry.Key, out var previous) && previous.Path.Length > 0)
        {
            _byPath.Remove(previous.Path);
        }

        _byKey[entry.Key] = entry;
        if (entry.Outcome == LedgerOutcome.Published && entry.Path.Length > 0)
        {
            _byPath[entry.Path] = entry.Key;
        }
    }

    /// <summary>Forgets a link.</summary>
    /// <param name="key">The content key.</param>
    public void Remove(string key)
    {
        if (_byKey.Remove(key, out var entry) && entry.Path.Length > 0)
        {
            _byPath.Remove(entry.Path);
        }
    }

    private static bool StreamAccessKey(string key) => Streaming.StreamAccess.IsValidKey(key);
}
