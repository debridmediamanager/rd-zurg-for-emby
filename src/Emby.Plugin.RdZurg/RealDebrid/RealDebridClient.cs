using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Emby.Plugin.RdZurg.RealDebrid;

/// <summary>
/// Raised when Real-Debrid refuses a request in a way that means stop, not retry.
/// </summary>
public class RealDebridRefusedException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="RealDebridRefusedException"/> class.</summary>
    /// <param name="message">What was refused.</param>
    public RealDebridRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RealDebridRefusedException"/> class.</summary>
    public RealDebridRefusedException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="RealDebridRefusedException"/> class.</summary>
    /// <param name="message">What was refused.</param>
    /// <param name="innerException">The underlying failure.</param>
    public RealDebridRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The Real-Debrid calls a library needs, paced to the account's published budget.
/// </summary>
/// <remarks>
/// Real-Debrid allows 250 requests a minute, counts refused requests against that allowance, and
/// publishes no cooldown - the documentation's words are "blocked for undefined amount of time".
/// So every call in the process passes through one gate, and a 429 stops the caller rather than
/// starting a retry loop that digs deeper.
/// </remarks>
public sealed class RealDebridClient
{
    private const string Base = "https://api.real-debrid.com/rest/1.0";

    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static DateTime _lastCall = DateTime.MinValue;
    private static DateTime _lastUnrestrict = DateTime.MinValue;
    private static DateTime _blockedUntil = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly TimeSpan _minInterval;

    /// <summary>Initializes a new instance of the <see cref="RealDebridClient"/> class.</summary>
    /// <param name="http">The client to send with.</param>
    /// <param name="token">The account's API token.</param>
    /// <param name="minRequestIntervalMs">Minimum gap between calls, in milliseconds.</param>
    public RealDebridClient(HttpClient http, string token, int minRequestIntervalMs = 300)
    {
        _http = http;
        _token = token;
        _minInterval = TimeSpan.FromMilliseconds(Math.Max(minRequestIntervalMs, 300));
    }

    /// <summary>
    /// Lists the account's torrents, newest first.
    /// </summary>
    /// <param name="max">How many to take. Zero or less takes all of them.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The torrents.</returns>
    public async Task<IReadOnlyList<RdTorrent>> GetTorrentsAsync(int max, CancellationToken cancellationToken)
    {
        var all = new List<RdTorrent>();
        var page = 1;
        const int Limit = 100;
        long? expectedCount = null;
        var ids = new HashSet<string>(StringComparer.Ordinal);

        while (max <= 0 || all.Count < max)
        {
            var url = string.Format(CultureInfo.InvariantCulture, "{0}/torrents?page={1}&limit={2}", Base, page, Limit);
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);

            if (response.Headers.TryGetValues("X-Total-Count", out var values)
                && long.TryParse(values.FirstOrDefault(), CultureInfo.InvariantCulture, out var count))
            {
                if (expectedCount.HasValue && expectedCount != count)
                    throw new IOException("The account changed during pagination; sync again before cleanup.");
                expectedCount = count;
            }

            // Past the end is a 204, not an empty array, and page 0 is a 204 as well - hence page 1.
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                break;
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var batch = JsonSerializer.Deserialize<List<RdTorrent>>(body, _json);

            if (batch is null) throw new IOException("Real-Debrid returned a null torrent listing.");
            if (batch.Count == 0)
            {
                break;
            }

            if (batch.Any(t => !ids.Add(t.Id)))
                throw new IOException("Real-Debrid returned overlapping pages; cleanup is unsafe.");
            all.AddRange(batch);
            page++;
        }

        if (max <= 0 && expectedCount.HasValue && all.Count != expectedCount)
            throw new IOException("Real-Debrid returned an incomplete listing; cleanup is unsafe.");

        if (max > 0 && all.Count > max)
        {
            all.RemoveRange(max, all.Count - max);
        }

        return all;
    }

    /// <summary>Reads one torrent's detail, including its file list.</summary>
    /// <param name="id">The torrent id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The torrent, or <c>null</c> when Real-Debrid does not know it.</returns>
    public async Task<RdTorrentInfo?> GetTorrentInfoAsync(string id, CancellationToken cancellationToken)
    {
        var url = string.Format(CultureInfo.InvariantCulture, "{0}/torrents/info/{1}", Base, id);
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<RdTorrentInfo>(body, _json);
    }

    /// <summary>
    /// Turns a stored link into a CDN URL that serves bytes.
    /// </summary>
    /// <param name="linkKey">The 13 character content key.</param>
    /// <param name="clientIp">The address that will pull the bytes, when it is a routable one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The unrestricted link.</returns>
    /// <remarks>
    /// Real-Debrid records the downloading address and treats one account's links being pulled from
    /// many addresses as sharing, so the link is minted for whoever will actually fetch it.
    /// </remarks>
    public async Task<RdUnrestricted?> UnrestrictAsync(string linkKey, string? clientIp, CancellationToken cancellationToken)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("link", "https://real-debrid.com/d/" + linkKey)
        };

        if (!string.IsNullOrEmpty(clientIp))
        {
            form.Add(new KeyValuePair<string, string>("ip", clientIp));
        }

        using var content = new FormUrlEncodedContent(form);
        using var response = await SendAsync(HttpMethod.Post, Base + "/unrestrict/link", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new RealDebridRefusedException(
                string.Format(CultureInfo.InvariantCulture, "Real-Debrid unrestrict returned HTTP {0}.", (int)response.StatusCode));
        }

        return JsonSerializer.Deserialize<RdUnrestricted>(body, _json);
    }

    /// <summary>
    /// Reduces a stored link to its 13 character content key.
    /// </summary>
    /// <param name="link">A <c>/d/</c> link or bare id, of either length.</param>
    /// <returns>The content key.</returns>
    /// <remarks>
    /// A <c>/d/</c> id is 13 characters of content key plus, sometimes, 3 characters that bind it to
    /// the account that made it. The bare 13 are what stays valid, so that is what gets stored.
    /// </remarks>
    public static string LinkKey(string link)
    {
        ArgumentNullException.ThrowIfNull(link);

        var id = link.AsSpan();
        var slash = id.LastIndexOf('/');
        if (slash >= 0)
        {
            id = id[(slash + 1)..];
        }

        return id.Length > 13 ? id[..13].ToString() : id.ToString();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        await PaceAsync(url.EndsWith("/unrestrict/link", StringComparison.Ordinal), cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = content;

        var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            _blockedUntil = DateTime.UtcNow.AddMinutes(10);
            response.Dispose();
            throw new RealDebridRefusedException("Real-Debrid answered 429. Refused requests count against the budget, so this pass stops here.");
        }

        return response;
    }

    private async Task PaceAsync(bool unrestrict, CancellationToken cancellationToken)
    {
        if (_minInterval <= TimeSpan.Zero)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_blockedUntil > DateTime.UtcNow)
                throw new RealDebridRefusedException("Real-Debrid requests are paused after a rate-limit response. Try again later.");
            var wait = _minInterval - (DateTime.UtcNow - _lastCall);
            if (unrestrict)
            {
                var linkWait = TimeSpan.FromSeconds(5) - (DateTime.UtcNow - _lastUnrestrict);
                if (linkWait > wait) wait = linkWait;
            }
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            if (_blockedUntil > DateTime.UtcNow)
                throw new RealDebridRefusedException("Real-Debrid requests are paused after a rate-limit response.");
            _lastCall = DateTime.UtcNow;
            if (unrestrict) _lastUnrestrict = _lastCall;
        }
        finally
        {
            _gate.Release();
        }
    }
}
