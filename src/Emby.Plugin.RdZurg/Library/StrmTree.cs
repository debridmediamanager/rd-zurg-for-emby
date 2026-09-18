using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>What one pass did to the tree.</summary>
/// <param name="Written">Files created.</param>
/// <param name="Rewritten">Files whose URL changed.</param>
/// <param name="Unchanged">Files left alone.</param>
/// <param name="Deleted">Files removed.</param>
/// <param name="CleanupRefused">Whether a mass deletion was refused.</param>
public readonly record struct TreeChanges(int Written, int Rewritten, int Unchanged, int Deleted, bool CleanupRefused);

/// <summary>
/// The tree of <c>.strm</c> files the plugin owns.
/// </summary>
/// <remarks>
/// <para>
/// Emby stores a <c>.strm</c> file's target and re-reads it when the file changes, so a file is only ever
/// rewritten when its URL actually differs: touching one for no reason makes Emby re-probe the item, and
/// re-probing costs a Real-Debrid call per file.
/// </para>
/// <para>
/// Writes land through a staging directory on the same filesystem, so a scan never sees half a file, and
/// deletions come last, so a release that moved is never briefly absent.
/// </para>
/// </remarks>
public sealed class StrmTree
{
    /// <summary>The file that marks a directory as this plugin's to write in.</summary>
    public const string MarkerName = ".rd-zurg";

    private const string StagingName = ".staging";

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="StrmTree"/> class.</summary>
    /// <param name="root">The tree root, which the plugin owns entirely.</param>
    /// <param name="logger">Logger.</param>
    public StrmTree(string root, ILogger logger)
    {
        _root = root;
        _logger = logger;
    }

    /// <summary>Gets the tree root.</summary>
    public string Root => _root;

    /// <summary>Creates the tree and marks it as this plugin's.</summary>
    public void Ensure()
    {
        Directory.CreateDirectory(Path.Combine(_root, StrmPaths.Movies));
        Directory.CreateDirectory(Path.Combine(_root, StrmPaths.Shows));
        Directory.CreateDirectory(Path.Combine(_root, StagingName));

        foreach (var directory in new[] { StrmPaths.Movies, StrmPaths.Shows })
        {
            var marker = Path.Combine(_root, directory, MarkerName);
            if (!File.Exists(marker))
            {
                File.WriteAllText(
                    marker,
                    "This directory belongs to the RD zurg plugin. Every .strm file in it is written from your\n"
                    + "Real-Debrid account and will be rewritten or removed on the next sync.\n",
                    _utf8);
            }
        }
    }

    /// <summary>Reports whether the tree is this plugin's to write in.</summary>
    /// <returns>Whether both markers are in place.</returns>
    public bool IsOwned()
        => File.Exists(Path.Combine(_root, StrmPaths.Movies, MarkerName))
            && File.Exists(Path.Combine(_root, StrmPaths.Shows, MarkerName));

    /// <summary>Every <c>.strm</c> file the plugin wrote, by path relative to the root.</summary>
    /// <returns>The paths and their contents.</returns>
    public IReadOnlyDictionary<string, string> Published()
    {
        var published = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_root))
        {
            return published;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*.strm", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + StagingName + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                continue;
            }

            published[Path.GetRelativePath(_root, file).Replace('\\', '/')] = File.ReadAllText(file).Trim();
        }

        return published;
    }

    /// <summary>
    /// Makes the tree match <paramref name="desired"/>.
    /// </summary>
    /// <param name="desired">Every file that should exist, by relative path, with the URL it should hold.</param>
    /// <param name="mayDelete">Whether this pass proved what is gone.</param>
    /// <param name="allowLargeCleanup">Whether a mass deletion is expected.</param>
    /// <returns>What changed.</returns>
    public TreeChanges Apply(IReadOnlyDictionary<string, string> desired, bool mayDelete, bool allowLargeCleanup)
    {
        ArgumentNullException.ThrowIfNull(desired);

        if (!IsOwned())
        {
            throw new InvalidOperationException("The library tree is missing its " + MarkerName + " marker; refusing to write.");
        }

        var existing = Published();
        var written = 0;
        var rewritten = 0;
        var unchanged = 0;

        foreach (var (path, url) in desired)
        {
            if (existing.TryGetValue(path, out var current))
            {
                if (string.Equals(current, url, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                Write(path, url);
                rewritten++;
                continue;
            }

            Write(path, url);
            written++;
        }

        var deleted = 0;
        var refused = false;
        if (mayDelete)
        {
            var stale = existing.Keys.Where(p => !desired.ContainsKey(p)).ToList();

            // One transient empty answer from Real-Debrid would otherwise empty the library, and Emby would
            // then delete every item along with what it knows about them.
            if (!allowLargeCleanup && stale.Count > 20 && stale.Count > existing.Count / 10)
            {
                _logger.Warn(
                    "RD zurg: refusing to delete {0} of {1} files in one pass. Turn on \"Allow large cleanups\" if the account really shrank that much.",
                    stale.Count,
                    existing.Count);
                refused = true;
            }
            else
            {
                deleted = stale.Count(Delete);
                PruneEmptyDirectories();
            }
        }

        return new TreeChanges(written, rewritten, unchanged, deleted, refused);
    }

    /// <summary>Reads the content of one published file.</summary>
    /// <param name="path">A path relative to the root.</param>
    /// <returns>The URL, or <c>null</c>.</returns>
    public string? Read(string path)
    {
        var full = Full(path);
        return File.Exists(full) ? File.ReadAllText(full).Trim() : null;
    }

    private void Write(string path, string url)
    {
        var destination = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        // Staged on the same filesystem, so the rename is atomic and a scan never reads half a file.
        var staging = Path.Combine(_root, StagingName);
        Directory.CreateDirectory(staging);
        var temp = Path.Combine(staging, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".strm");
        File.WriteAllText(temp, url, _utf8);
        File.Move(temp, destination, true);
    }

    private bool Delete(string path)
    {
        var full = Full(path);
        try
        {
            // Only ever a file this plugin wrote: it has to hold one of our own signed URLs.
            if (!File.Exists(full) || StreamUrls.KeyOf(File.ReadAllText(full)) is null)
            {
                return false;
            }

            File.Delete(full);
            return true;
        }
        catch (IOException ex)
        {
            _logger.Warn("RD zurg: could not remove {0}: {1}", path, ex.GetType().Name);
            return false;
        }
    }

    private void PruneEmptyDirectories()
    {
        foreach (var library in new[] { StrmPaths.Movies, StrmPaths.Shows })
        {
            var root = Path.Combine(_root, library);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
        }
    }

    private string Full(string path) => Path.Combine(_root, path.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Reads what this plugin wrote back out of a <c>.strm</c> file.</summary>
public static class StreamUrls
{
    private static readonly Regex _url = new(
        @"/RdZurg/Stream/(?<key>[A-Z0-9]{13})(?:/|\?|$)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Reads the release's file name back out of a stream URL.</summary>
    /// <param name="content">A <c>.strm</c> file's contents.</param>
    /// <returns>The name, or <c>null</c> when this is not one of ours.</returns>
    public static string? FileNameOf(string? content)
    {
        if (KeyOf(content) is null || !Uri.TryCreate(content!.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var name = Uri.UnescapeDataString(uri.AbsolutePath[(uri.AbsolutePath.LastIndexOf('/') + 1)..]);
        return name.Length > 0 ? name : null;
    }

    /// <summary>Reads the content key out of a stream URL.</summary>
    /// <param name="content">A <c>.strm</c> file's contents.</param>
    /// <returns>The key, or <c>null</c> when this is not one of ours.</returns>
    public static string? KeyOf(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var match = _url.Match(content.Trim());
        return match.Success ? match.Groups["key"].Value : null;
    }
}
