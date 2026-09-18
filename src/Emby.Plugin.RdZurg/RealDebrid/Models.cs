using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Emby.Plugin.RdZurg.RealDebrid;

/// <summary>A torrent as the account listing reports it.</summary>
public class RdTorrent
{
    /// <summary>Gets or sets Real-Debrid's id for the torrent.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the release name.</summary>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>Gets or sets the infohash.</summary>
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    /// <summary>Gets or sets the total size in bytes.</summary>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>Gets or sets the status, of which only <c>downloaded</c> is servable.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets when the torrent was added. Reported in UTC+2 despite the Z suffix.</summary>
    [JsonPropertyName("added")]
    public string Added { get; set; } = string.Empty;

    /// <summary>Gets or sets one link per selected file, in selection order.</summary>
    [JsonPropertyName("links")]
    public IList<string> Links { get; set; } = new List<string>();
}

/// <summary>One file within a torrent.</summary>
public class RdFile
{
    /// <summary>Gets or sets the file's id within the torrent.</summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Gets or sets the path within the torrent, always leading with a slash.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the size Real-Debrid reports.
    /// </summary>
    /// <remarks>
    /// This is the size of the content, which is not always the size of the file the link serves:
    /// a release wrapped in a RAR reports the inner size here and serves the larger archive.
    /// </remarks>
    [JsonPropertyName("bytes")]
    public long Bytes { get; set; }

    /// <summary>Gets or sets whether the file was selected, and so has a link.</summary>
    [JsonPropertyName("selected")]
    public int Selected { get; set; }
}

/// <summary>A torrent with its file list.</summary>
public class RdTorrentInfo : RdTorrent
{
    /// <summary>Gets or sets every file in the torrent, selected or not.</summary>
    [JsonPropertyName("files")]
    public IList<RdFile> Files { get; set; } = new List<RdFile>();
}

/// <summary>What unrestricting a link returns.</summary>
public class RdUnrestricted
{
    /// <summary>Gets or sets the CDN URL that actually serves bytes.</summary>
    [JsonPropertyName("download")]
    public string Download { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the filename the CDN will serve.
    /// </summary>
    /// <remarks>This is the name to trust. The torrent's file list can disagree with it.</remarks>
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of the file the CDN will serve.</summary>
    [JsonPropertyName("filesize")]
    public long Filesize { get; set; }

    /// <summary>Gets or sets the MIME type, when Real-Debrid provides one.</summary>
    [JsonPropertyName("mimeType")]
    public string? MimeType { get; set; }
}

/// <summary>The account a token belongs to.</summary>
public class RdUser
{
    /// <summary>Gets or sets the account's numeric id, which outlives any one token.</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Gets or sets the account's username.</summary>
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
}
