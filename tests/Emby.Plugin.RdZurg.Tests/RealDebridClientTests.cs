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
