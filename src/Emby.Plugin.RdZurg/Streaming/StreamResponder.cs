using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.RealDebrid;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Streaming;

/// <summary>One playback request, as the responder sees it; the host adapts its own request and response to this.</summary>
public interface IStreamExchange
{
    /// <summary>Gets the HTTP method, GET or HEAD.</summary>
    string Method { get; }

    /// <summary>Gets the Range header, if any.</summary>
    string? Range { get; }

    /// <summary>Gets a value indicating whether the request carries If-Range.</summary>
    bool HasIfRange { get; }

    /// <summary>Gets the signature from the query string.</summary>
    string? Signature { get; }

    /// <summary>Gets a value indicating whether the status line and headers are already on the wire.</summary>
    bool HeadersSent { get; }

    /// <summary>Sets the status code.</summary>
    /// <param name="status">The status.</param>
    void SetStatus(int status);

    /// <summary>Sets a response header.</summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    void SetHeader(string name, string value);

    /// <summary>Removes a response header set earlier.</summary>
    /// <param name="name">The name.</param>
    void RemoveHeader(string name);

    /// <summary>Sets the Content-Length.</summary>
    /// <param name="length">The length.</param>
    void SetContentLength(long length);

    /// <summary>Writes body bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken);
}

/// <summary>
/// Serves a signed playback capability: validates, resolves, checks the CDN's answer before any header goes out,
/// and copies the requested bytes. Knows nothing about the host.
/// </summary>
public sealed class StreamResponder
{
    private readonly LinkResolver _resolver;
    private readonly HttpClient _http;
    private readonly Func<PluginOptions?> _options;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="StreamResponder"/> class.</summary>
    /// <param name="resolver">The plugin's one resolver.</param>
    /// <param name="http">The client for CDN requests.</param>
    /// <param name="options">Reads the current settings.</param>
    /// <param name="logger">Logger.</param>
    public StreamResponder(LinkResolver resolver, HttpClient http, Func<PluginOptions?> options, ILogger logger)
    {
        _resolver = resolver;
        _http = http;
        _options = options;
        _logger = logger;
    }

    /// <summary>Gets or sets how long an upstream read may stall before the request fails.</summary>
    /// <remarks>
    /// Only reads from the CDN count. A paused player or a throttled transcode stops reading for minutes, and
    /// that is not the CDN's failure.
    /// </remarks>
    public TimeSpan UpstreamInactivity { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Answers one request.</summary>
    /// <param name="linkKey">The content key from the path.</param>
    /// <param name="exchange">The request and response.</param>
    /// <param name="cancellationToken">Cancelled when the client goes away.</param>
    /// <returns>A task that completes when the response is written, or throws to abort a response already started.</returns>
    public async Task RespondAsync(string linkKey, IStreamExchange exchange, CancellationToken cancellationToken)
    {
        exchange.SetHeader("Cache-Control", "no-store");
        exchange.SetHeader("Referrer-Policy", "no-referrer");

        var options = _options();
        if (options is null || string.IsNullOrWhiteSpace(options.ApiKey))
        {
            Fail(exchange, 503);
            return;
        }

        if (!StreamAccess.Verify(options.StreamSecret, options.AccountId, linkKey, exchange.Signature))
        {
            Fail(exchange, 401);
            return;
        }

        try
        {
            var resolved = await _resolver.ResolveAsync(linkKey, cancellationToken).ConfigureAwait(false);
            if (!resolved.RequiresStreaming && options.RedirectDirectStreams)
            {
                exchange.SetStatus(302);
                exchange.SetHeader("Location", resolved.Url);
                exchange.SetContentLength(0);
                return;
            }

            await ServeAsync(resolved, exchange, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is OperationCanceledException or IOException)
        {
            // A player closes its old read when seeking. The source is still valid.
            Fail(exchange, 499);
        }
        catch (InvalidDataException)
        {
            _resolver.Invalidate(linkKey);
            Fail(exchange, 422);
        }
        catch (Exception ex) when (ex is HttpRequestException or RealDebridRefusedException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        {
            _resolver.Invalidate(linkKey);

            // HTTP exceptions can contain signed URLs. Log only the type.
            _logger.Warn("RD zurg: playback source failed ({0})", ex.GetType().Name);
            Fail(exchange, cancellationToken.IsCancellationRequested ? 499 : 502);
        }
    }

    private static void Fail(IStreamExchange exchange, int status)
    {
        if (exchange.HeadersSent)
        {
            // The status line promised bytes that will not come; only a broken connection says so.
            throw new IOException("The playback source failed after the response started.");
        }

        exchange.RemoveHeader("Content-Range");
        exchange.RemoveHeader("Content-Type");
        exchange.SetStatus(status);
        exchange.SetContentLength(0);
    }

    private async Task ServeAsync(ResolvedLink resolved, IStreamExchange exchange, CancellationToken cancellationToken)
    {
        var length = resolved.PlayableLength;
        var isGet = string.Equals(exchange.Method, "GET", StringComparison.OrdinalIgnoreCase);

        // Range applies only to GET. With no entity validators we cannot satisfy If-Range.
        var hasRange = isGet && !string.IsNullOrEmpty(exchange.Range) && !exchange.HasIfRange;
        ByteRange? parsed = null;
        if (hasRange && !ByteRange.TryParse(exchange.Range, length, out parsed))
        {
            exchange.SetHeader("Content-Range", string.Format(CultureInfo.InvariantCulture, "bytes */{0}", length));
            exchange.SetStatus(416);
            exchange.SetContentLength(0);
            return;
        }

        var requested = parsed ?? new ByteRange(0, length - 1);
        if (!isGet)
        {
            SetHeaders(resolved, requested, hasRange, exchange);
            return;
        }

        var offset = resolved.ArchiveEntry?.DataOffset ?? 0;
        using var upstream = new HttpRequestMessage(HttpMethod.Get, resolved.Url);
        upstream.Headers.Range = new RangeHeaderValue(offset + requested.From, offset + requested.To);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(UpstreamInactivity);
        using var response = await _http.SendAsync(upstream, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        var range = response.Content.Headers.ContentRange;
        if (response.StatusCode != HttpStatusCode.PartialContent
            || range?.Unit != "bytes" || range.From != offset + requested.From
            || range.To != offset + requested.To || range.Length != resolved.Size
            || (response.Content.Headers.ContentLength is long received && received != requested.Length))
        {
            _logger.Warn(
                "RD zurg: CDN range mismatch: HTTP {0}, received {1}, expected {2}-{3}/{4}",
                (int)response.StatusCode,
                range?.ToString() ?? "none",
                offset + requested.From,
                offset + requested.To,
                resolved.Size);
            throw new HttpRequestException("The CDN did not honor the requested byte range.");
        }

        SetHeaders(resolved, requested, hasRange, exchange);
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var buffer = new byte[64 * 1024];
        var remaining = requested.Length;
        while (remaining > 0)
        {
            timeout.CancelAfter(UpstreamInactivity);
            var read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), timeout.Token).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The CDN truncated the requested range.");
            }

            // Writing to a paused player can take as long as the pause; that is not an upstream stall.
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await exchange.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }

    private static void SetHeaders(ResolvedLink resolved, ByteRange requested, bool partial, IStreamExchange exchange)
    {
        exchange.SetHeader("Accept-Ranges", "bytes");
        exchange.SetHeader("Content-Type", ContentTypes.For(resolved.FileName));
        exchange.SetStatus(partial ? 206 : 200);
        exchange.SetContentLength(requested.Length);
        if (partial)
        {
            exchange.SetHeader("Content-Range", string.Format(CultureInfo.InvariantCulture, "bytes {0}-{1}/{2}", requested.From, requested.To, resolved.PlayableLength));
        }
    }
}
