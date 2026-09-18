using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.Archive;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.Streaming;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

/// <summary>
/// The playback contract, as the Jellyfin plugin's own safety tests fixed it: nothing reaches Real-Debrid without
/// a valid signature, a range is either served exactly or refused, and a CDN that ignores the range is an error
/// rather than media.
/// </summary>
public class StreamingSafetyTests
{
    private const string Key = "ZH4JR4PYJ6S2C";
    private const string Secret = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
    private const string Account = "1234567";

    [Fact]
    public async Task UnsignedRequestCannotSpendTheAccountToken()
    {
        var (responder, exchange, handler) = Responder(authorized: false);
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(401, exchange.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnUnconfiguredPluginRefusesBeforeAnythingElse()
    {
        var (responder, exchange, handler) = Responder(token: string.Empty);
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(503, exchange.Status);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task UnsatisfiableRangeReturns416WithoutReadingUpstream()
    {
        var (responder, exchange, handler) = Responder();
        exchange.Range = "bytes=100-";
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(416, exchange.Status);
        Assert.Equal("bytes */100", exchange.Headers["Content-Range"]);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AnUpstreamIgnoringRangeCannotBeServedAsMedia()
    {
        var (responder, exchange, _) = Responder();
        exchange.Range = "bytes=0-3";
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(502, exchange.Status);
        Assert.Equal(0, exchange.Body.Length);
        Assert.False(exchange.Headers.ContainsKey("Content-Range"));
        Assert.Equal(0, exchange.ContentLength);
    }

    [Theory]
    [InlineData("bytes=0-3", 10, 13)]
    [InlineData("bytes=-4", 106, 109)]
    public async Task ValidRangesReturnOnlyTheRequestedMedia(string range, long from, long to)
    {
        var (responder, exchange, handler) = Responder();
        exchange.Range = range;
        handler.Respond = request =>
        {
            Assert.Equal(from, Assert.Single(request.Headers.Range!.Ranges).From);
            Assert.Equal(to, Assert.Single(request.Headers.Range.Ranges).To);
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, 117);
            return response;
        };

        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(206, exchange.Status);
        Assert.Equal(4, exchange.Body.Length);
        Assert.Equal("video/x-matroska", exchange.Headers["Content-Type"]);
    }

    [Fact]
    public async Task HeadDoesNotDownloadTheMember()
    {
        var (responder, exchange, handler) = Responder();
        exchange.Method = "HEAD";
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(100, exchange.ContentLength);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task HeadIgnoresRangeHeaders()
    {
        var (responder, exchange, _) = Responder();
        exchange.Method = "HEAD";
        exchange.Range = "bytes=0-3";
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(200, exchange.Status);
        Assert.Equal(100, exchange.ContentLength);
    }

    [Fact]
    public async Task PlayerCancellationKeepsTheCachedSource()
    {
        var (responder, exchange, handler) = Responder();
        using var cancellation = new CancellationTokenSource();
        handler.Respond = _ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        };

        await responder.RespondAsync(Key, exchange, cancellation.Token);
        Assert.Equal(499, exchange.Status);

        // HEAD must reuse the cached resolution, without trying to unrestrict the test URL.
        var head = new FakeExchange { Method = "HEAD", Signature = exchange.Signature };
        await responder.RespondAsync(Key, head, CancellationToken.None);
        Assert.Equal(200, head.Status);
        Assert.Equal(1, handler.Calls);
    }

    /// <summary>
    /// A paused player or a throttled transcode stops reading for minutes. The inactivity timeout is the CDN's,
    /// and must not be spent waiting for the client. (The Jellyfin plugins time the client out here.)
    /// </summary>
    [Fact]
    public async Task APausedPlayerDoesNotAbortTheStream()
    {
        var (responder, exchange, handler) = Responder();
        responder.UpstreamInactivity = TimeSpan.FromMilliseconds(250);
        exchange.WriteDelay = TimeSpan.FromMilliseconds(900);
        exchange.Range = "bytes=0-3";
        handler.Respond = _ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(new byte[] { 1, 2, 3, 4 }) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(10, 13, 117);
            return response;
        };

        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(206, exchange.Status);
        Assert.Equal(4, exchange.Body.Length);
    }

    [Fact]
    public async Task APlainFileCanBeHandedToTheCdnInstead()
    {
        var (responder, exchange, handler) = Responder(redirect: true, archive: false);
        await responder.RespondAsync(Key, exchange, CancellationToken.None);
        Assert.Equal(302, exchange.Status);
        Assert.Equal("https://cdn.example.test/movie.mkv", exchange.Headers["Location"]);
        Assert.Equal(0, handler.Calls);
    }

    private static (StreamResponder Responder, FakeExchange Exchange, Handler Handler) Responder(
        bool authorized = true, string token = "test-token", bool redirect = false, bool archive = true)
    {
        var options = new PluginOptions { ApiKey = token, StreamSecret = Secret, AccountId = Account, RedirectDirectStreams = redirect };
        var handler = new Handler();
        var http = new HttpClient(handler, false);
        var logger = new FakeLogger();
        var resolver = new LinkResolver(http, () => options, logger);
        resolver.Remember(Key, new ResolvedLink
        {
            Url = archive ? "https://cdn.example.test/movie.rar" : "https://cdn.example.test/movie.mkv",
            FileName = "movie.mkv",
            Size = 117,
            ExpiresUtc = DateTime.UtcNow.AddMinutes(1),
            ArchiveEntry = archive ? new RarEntry { Name = "movie.mkv", DataOffset = 10, Length = 100, IsStored = true } : null,
        });

        var exchange = new FakeExchange
        {
            Signature = authorized ? StreamAccess.Sign(Secret, Account, Key) : null,
        };

        return (new StreamResponder(resolver, http, () => options, logger), exchange, handler);
    }

    private sealed class Handler : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; }
            = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[117]) };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Respond(request));
        }
    }

    private sealed class FakeExchange : IStreamExchange
    {
        public string Method { get; set; } = "GET";

        public string? Range { get; set; }

        public bool HasIfRange { get; set; }

        public string? Signature { get; set; }

        public bool HeadersSent { get; private set; }

        public int Status { get; private set; }

        public long? ContentLength { get; private set; }

        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MemoryStream Body { get; } = new();

        public TimeSpan WriteDelay { get; set; }

        public void SetStatus(int status) => Status = status;

        public void SetHeader(string name, string value) => Headers[name] = value;

        public void RemoveHeader(string name) => Headers.Remove(name);

        public void SetContentLength(long length) => ContentLength = length;

        public async Task WriteAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken)
        {
            if (WriteDelay > TimeSpan.Zero)
            {
                await Task.Delay(WriteDelay, cancellationToken).ConfigureAwait(false);
            }

            HeadersSent = true;
            await Body.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        }
    }
}
