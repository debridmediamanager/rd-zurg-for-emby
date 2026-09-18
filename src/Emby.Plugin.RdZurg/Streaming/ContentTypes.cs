using System;
using System.Collections.Generic;
using System.IO;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>The media types of the video containers a release can hold.</summary>
public static class ContentTypes
{
    private static readonly Dictionary<string, string> _types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mkv"] = "video/x-matroska",
        [".mp4"] = "video/mp4",
        [".m4v"] = "video/mp4",
        [".avi"] = "video/x-msvideo",
        [".ts"] = "video/mp2t",
        [".mov"] = "video/quicktime",
        [".wmv"] = "video/x-ms-wmv",
        [".mpg"] = "video/mpeg",
        [".mpeg"] = "video/mpeg",
        [".flv"] = "video/x-flv",
        [".webm"] = "video/webm",
    };

    /// <summary>Picks the media type for a file name.</summary>
    /// <param name="fileName">The name.</param>
    /// <returns>The type, or <c>application/octet-stream</c>.</returns>
    public static string For(string fileName)
        => _types.TryGetValue(Path.GetExtension(fileName), out var type) ? type : "application/octet-stream";
}
