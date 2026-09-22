using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.RealDebrid;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class RealDebridClientTests
{
    [Fact]
    public async Task ATruncatedListingCannotAuthorizeCleanup()
    {
        using var http = new HttpClient(new ListingHandler(false));
        var client = new RealDebridClient(http, "test");
        await Assert.ThrowsAsync<IOException>(() => client.GetTorrentsAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task OverlappingPagesCannotAuthorizeCleanup()
    {
        using var http = new HttpClient(new ListingHandler(true));
        var client = new RealDebridClient(http, "test");
        await Assert.ThrowsAsync<IOException>(() => client.GetTorrentsAsync(0, CancellationToken.None));
    }

    [Fact]
    public async Task DetailServiceFailureMustFailTheSync()
    {
        using var http = new HttpClient(new FailureHandler());
        var client = new RealDebridClient(http, "test");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetTorrentInfoAsync("torrent", CancellationToken.None));
    }

    /// <summary>
    /// The plugin's HTTP client has no timeout, because it also streams. A call Real-Debrid never answers has to fail
    /// rather than hang: the sync would never finish, and every cold playback waits behind the resolver's one gate.
    /// </summary>
    [Fact]
    public async Task AnUnansweredCallFailsInsteadOfHanging()
    {
        using var http = new HttpClient(new SilentHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var client = new RealDebridClient(http, "test") { RequestTimeout = TimeSpan.FromMilliseconds(300) };

        var listing = client.GetTorrentsAsync(0, CancellationToken.None);
        var unrestrict = client.UnrestrictAsync("ZH4JR4PYJ6S2C", null, CancellationToken.None);
        await Task.WhenAny(Task.WhenAll(listing, unrestrict), Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.True(listing.IsCompleted && unrestrict.IsCompleted, "a call Real-Debrid never answered is still waiting");
        await Assert.ThrowsAsync<HttpRequestException>(() => listing);
        await Assert.ThrowsAsync<HttpRequestException>(() => unrestrict);
    }

    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class ListingHandler(bool overlap) : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(++_calls == 1 || overlap ? HttpStatusCode.OK : HttpStatusCode.NoContent)
            {
                Content = new StringContent("[{\"id\":\"test-torrent\",\"status\":\"downloaded\",\"links\":[]}]")
            };
            response.Headers.Add("X-Total-Count", "2");
            return Task.FromResult(response);
        }
    }

    private sealed class FailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
