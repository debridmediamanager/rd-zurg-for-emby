using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.Archive;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.RealDebrid;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>
/// Turns a stored link into something playable, and remembers the answer.
/// </summary>
/// <remarks>
/// <para>
/// Two reasons this caches. Unrestricting has its own throttle far tighter than the documented request budget,
/// and a single playback session asks more than once - a probe, then the player, then every seek that reopens
/// the source. Reading an archive's headers costs a range request on top, so that answer is worth keeping for the
/// same period.
/// </para>
/// <para>
/// Emby builds a service object per request, so the cache only works if one resolver outlives them all: the
/// plugin owns a single instance.
/// </para>
/// </remarks>
public sealed class LinkResolver
{
    private static readonly TimeSpan _lifetime = TimeSpan.FromMinutes(30);

    private readonly ConcurrentDictionary<string, ResolvedLink> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _http;
    private readonly Func<PluginOptions?> _options;
    private readonly ILogger _logger;
    private string _configurationKey;

    /// <summary>Initializes a new instance of the <see cref="LinkResolver"/> class.</summary>
    /// <param name="http">The client for Real-Debrid and CDN requests, shared for the plugin's lifetime.</param>
    /// <param name="options">Reads the current settings.</param>
    /// <param name="logger">Logger.</param>
    public LinkResolver(HttpClient http, Func<PluginOptions?> options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _configurationKey = ConfigurationKey();
    }

    /// <summary>Forgets a resolution, so the next request mints a fresh one.</summary>
    /// <param name="linkKey">The content key.</param>
    public void Invalidate(string linkKey) => _cache.TryRemove(linkKey, out _);

    /// <summary>Stores a resolution, as a warm cache would hold it.</summary>
    /// <param name="linkKey">The content key.</param>
    /// <param name="resolved">The resolution.</param>
    internal void Remember(string linkKey, ResolvedLink resolved) => _cache[linkKey] = resolved;

    /// <summary>
    /// Resolves a stored link, minting and probing only when there is nothing usable cached.
    /// </summary>
    /// <param name="linkKey">The 13 character content key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolution.</returns>
    public async Task<ResolvedLink> ResolveAsync(string linkKey, CancellationToken cancellationToken)
    {
        if (!StreamAccess.IsValidKey(linkKey))
        {
            throw new InvalidOperationException("Invalid content key.");
        }

        if (_configurationKey == ConfigurationKey()
            && _cache.TryGetValue(linkKey, out var warm) && warm.ExpiresUtc > DateTime.UtcNow)
        {
            return warm;
        }

        // Keep one gate for cold resolutions. Removing per-key locks while callers still wait
        // creates two independent locks for the same key after a failed resolution.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configurationKey = ConfigurationKey();
            if (_configurationKey != configurationKey)
            {
                _cache.Clear();
                _configurationKey = configurationKey;
            }

            if (_cache.TryGetValue(linkKey, out var cached) && cached.ExpiresUtc > DateTime.UtcNow)
            {
                return cached;
            }

            foreach (var key in _cache.Where(p => p.Value.ExpiresUtc <= DateTime.UtcNow).Select(p => p.Key))
            {
                _cache.TryRemove(key, out _);
            }

            if (_cache.Count >= 1024)
            {
                _cache.TryRemove(_cache.MinBy(p => p.Value.ExpiresUtc).Key, out _);
            }

            var resolved = await ResolveUncachedAsync(linkKey, cancellationToken).ConfigureAwait(false);
            _cache[linkKey] = resolved;
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ConfigurationKey()
    {
        var options = _options();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            options?.ApiKey + "|" + options?.UnwrapArchives + "|" + options?.RedirectDirectStreams)));
    }

    private async Task<ResolvedLink> ResolveUncachedAsync(string linkKey, CancellationToken cancellationToken)
    {
        var options = _options() ?? throw new InvalidOperationException("The plugin is not loaded.");

        if (string.IsNullOrEmpty(options.ApiKey))
        {
            throw new InvalidOperationException("No Real-Debrid API token is configured.");
        }

        var client = new RealDebridClient(_http, options.ApiKey, options.MinRequestIntervalMs);
        var unrestricted = await client.UnrestrictAsync(linkKey, null, cancellationToken).ConfigureAwait(false);

        if (unrestricted is null || string.IsNullOrEmpty(unrestricted.Download))
        {
            throw new RealDebridRefusedException("Real-Debrid returned no download URL for " + linkKey);
        }

        if (!Uri.TryCreate(unrestricted.Download, UriKind.Absolute, out var download)
            || (download.Scheme != Uri.UriSchemeHttp && download.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(download.UserInfo) || unrestricted.Filesize <= 0)
        {
            throw new InvalidDataException("Real-Debrid returned invalid media information.");
        }

        var entry = await ProbeArchiveAsync(unrestricted, cancellationToken).ConfigureAwait(false);
        if (entry is not null && !options.UnwrapArchives)
        {
            throw new InvalidDataException("This release is an archive and archive playback is disabled.");
        }

        if (entry is not null)
        {
            _logger.Info(
                "RD zurg: {0} is an archive; serving {1} from offset {2} ({3} bytes)",
                linkKey,
                entry.Name,
                entry.DataOffset,
                entry.Length);
        }

        return new ResolvedLink
        {
            Url = unrestricted.Download,
            FileName = entry?.Name ?? unrestricted.Filename,
            Size = unrestricted.Filesize,
            ExpiresUtc = DateTime.UtcNow.Add(_lifetime),
            ArchiveEntry = entry
        };
    }

    /// <summary>
    /// Reads the head of a release and, if it is a RAR, finds the member to serve.
    /// </summary>
    /// <remarks>
    /// The name is only a hint. Real-Debrid does serve <c>.mkv.rar</c>, but the check that decides
    /// is the signature in the bytes, because the file list has already been shown to lie about the
    /// name. A compressed member is reported as no member at all: its bytes are not playable as a
    /// range, and pretending otherwise produces a file that opens and then fails.
    /// </remarks>
    private async Task<RarEntry?> ProbeArchiveAsync(RdUnrestricted unrestricted, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Get, unrestricted.Download);
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, RarReader.HeaderProbeLength - 1);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent
            && response.Content.Headers.ContentRange?.From != 0)
        {
            throw new InvalidDataException("The CDN returned the wrong archive header range.");
        }

        // Some CDNs ignore Range. Never buffer their entire multi-gigabyte response.
        var head = new byte[RarReader.HeaderProbeLength];
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var count = 0;
        while (count < head.Length)
        {
            var read = await body.ReadAsync(head.AsMemory(count), timeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            count += read;
        }

        if (count < 8)
        {
            throw new InvalidDataException("The CDN returned an empty or truncated media header.");
        }

        if (!RarReader.LooksLikeRar(head.AsSpan(0, count)))
        {
            if (unrestricted.Filename.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The archive signature is missing.");
            }

            return null;
        }

        if (!RarReader.TryGetPrimaryEntry(head.AsSpan(0, count), out var entry)
            || entry.DataOffset > unrestricted.Filesize
            || entry.Length > unrestricted.Filesize - entry.DataOffset)
        {
            throw new InvalidDataException("This archive cannot be streamed: a complete, unencrypted stored video member is required.");
        }

        return entry;
    }

    /// <summary>Builds the stream URL a <c>.strm</c> file holds.</summary>
    /// <param name="baseUrl">Where Emby reaches its own API, without a trailing slash.</param>
    /// <param name="linkKey">The content key.</param>
    /// <param name="fileName">The release filename, whose extension tells ffmpeg the container.</param>
    /// <param name="secret">The hex signing key.</param>
    /// <param name="accountId">The Real-Debrid account id.</param>
    /// <returns>An absolute URL.</returns>
    public static string BuildUrl(string baseUrl, string linkKey, string fileName, string secret, string accountId)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(fileName);

        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}/RdZurg/Stream/{1}/{2}?signature={3}",
            baseUrl.TrimEnd('/'),
            linkKey,
            Uri.EscapeDataString(Path.GetFileName(fileName)),
            StreamAccess.Sign(secret, accountId, linkKey));
    }
}
