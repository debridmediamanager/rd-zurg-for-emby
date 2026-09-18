using System;
using Emby.Plugin.RdZurg.Archive;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>
/// What a stored link resolves to right now: a URL that serves bytes, and if the release turned out
/// to be an archive, the member inside it worth playing.
/// </summary>
public sealed class ResolvedLink
{
    /// <summary>Gets the CDN URL.</summary>
    public required string Url { get; init; }

    /// <summary>Gets the name the CDN serves, which the torrent's file list may disagree with.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the size of what the CDN serves, archive wrapper included.</summary>
    public required long Size { get; init; }

    /// <summary>Gets when this resolution should be thrown away.</summary>
    public required DateTime ExpiresUtc { get; init; }

    /// <summary>
    /// Gets the member to serve when the release is a readable archive, otherwise <c>null</c>.
    /// </summary>
    public RarEntry? ArchiveEntry { get; init; }

    /// <summary>Gets a value indicating whether the bytes have to pass through this server.</summary>
    public bool RequiresStreaming => ArchiveEntry is not null;

    /// <summary>Gets the length a player should be told about.</summary>
    public long PlayableLength => ArchiveEntry?.Length ?? Size;
}
