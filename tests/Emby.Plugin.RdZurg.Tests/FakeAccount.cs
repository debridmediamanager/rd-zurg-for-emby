using System;
using Emby.Plugin.RdZurg;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Plugin.RdZurg.Tests;

/// <summary>
/// Replays a captured Real-Debrid account: the same listing and torrent detail the Jellyfin plugin was measured
/// against, paged and answered the way the API does.
/// </summary>
internal sealed class FakeAccount : HttpMessageHandler
{
    private readonly Dictionary<string, JsonElement> _info = new(StringComparer.Ordinal);

    public FakeAccount(string fixture)
    {
        // A recorded account has no budget to protect, and 123 torrents at 300 ms each is a minute per pass.
        RealDebrid.RealDebridClient.MinimumInterval = TimeSpan.Zero;
        RealDebrid.RealDebridClient.UnrestrictInterval = TimeSpan.Zero;
        using var document = JsonDocument.Parse(Fixture.Text(fixture));
        Torrents = document.RootElement.GetProperty("listing").EnumerateArray().Select(t => t.Clone()).ToList();
        foreach (var entry in document.RootElement.GetProperty("info").EnumerateObject())
        {
            _info[entry.Name] = entry.Value.Clone();
        }
    }

    public List<JsonElement> Torrents { get; }

    public int DetailCalls { get; private set; }

    /// <summary>The torrents whose detail was asked for, in order.</summary>
    public List<string> Detailed { get; } = new();

    public int ListingCalls { get; private set; }

    /// <summary>Gets or sets a status the listing answers with instead, to fail a pass.</summary>
    public HttpStatusCode? ListingFailure { get; set; }

    /// <summary>Gets or sets what happens to the account right after a listing page is served, by page number.</summary>
    public Action<int>? AfterPage { get; set; }

    /// <summary>Gets or sets the account id the token belongs to.</summary>
    public string AccountId { get; set; } = "1234567";

    public void Remove(Func<JsonElement, bool> predicate) => Torrents.RemoveAll(t => predicate(t));

    /// <summary>Narrows the account to a few of its torrents.</summary>
    /// <param name="ids">The torrents to keep.</param>
    public void Keep(params string[] ids) => Torrents.RemoveAll(t => !ids.Contains(Id(t)));

    public static string Id(JsonElement torrent) => torrent.GetProperty("id").GetString()!;

    /// <summary>A recorded torrent's selected files, each with the link key it was given.</summary>
    /// <param name="id">The torrent id.</param>
    /// <returns>The files.</returns>
    public IReadOnlyList<(string Key, string Path, long Bytes, string Link)> SelectedFiles(string id)
    {
        var info = _info[id];
        var links = info.GetProperty("links").EnumerateArray().Select(l => l.GetString()!).ToList();
        return info.GetProperty("files").EnumerateArray()
            .Where(f => f.GetProperty("selected").GetInt32() == 1)
            .Select((f, i) => (RealDebrid.RealDebridClient.LinkKey(links[i]), f.GetProperty("path").GetString()!, f.GetProperty("bytes").GetInt64(), links[i]))
            .ToList();
    }

    /// <summary>
    /// Adds a torrent holding one recorded file on its own, as Real-Debrid lists the same bytes released alone: under
    /// the same link key.
    /// </summary>
    /// <param name="id">The new torrent's id.</param>
    /// <param name="path">The file's path, which names the release.</param>
    /// <param name="file">The recorded file.</param>
    public void AddSingleFileTorrent(string id, string path, (string Key, string Path, long Bytes, string Link) file)
    {
        var name = System.IO.Path.GetFileName(path);
        var listing = JsonSerializer.Serialize(new { id, filename = name, bytes = file.Bytes, status = "downloaded", links = new[] { file.Link } });
        var info = JsonSerializer.Serialize(new
        {
            id,
            filename = name,
            bytes = file.Bytes,
            status = "downloaded",
            files = new[] { new { id = 1, path = "/" + name, bytes = file.Bytes, selected = 1 } },
            links = new[] { file.Link },
        });
        Torrents.Insert(0, JsonDocument.Parse(listing).RootElement.Clone());
        _info[id] = JsonDocument.Parse(info).RootElement.Clone();
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath;

        if (path.EndsWith("/user", StringComparison.Ordinal))
        {
            return Json("{\"id\":" + AccountId + ",\"username\":\"tester\"}");
        }

        if (path.Contains("/torrents/info/", StringComparison.Ordinal))
        {
            DetailCalls++;
            var id = path[(path.LastIndexOf('/') + 1)..];
            Detailed.Add(id);
            return _info.TryGetValue(id, out var info) && Torrents.Any(t => Id(t) == id)
                ? Json(info.GetRawText())
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        if (path.EndsWith("/torrents", StringComparison.Ordinal))
        {
            ListingCalls++;
            if (ListingFailure is HttpStatusCode failure)
            {
                return Task.FromResult(new HttpResponseMessage(failure));
            }

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var page = int.Parse(query["page"] ?? "1", CultureInfo.InvariantCulture);
            var limit = int.Parse(query["limit"] ?? "100", CultureInfo.InvariantCulture);
            var slice = Torrents.Skip((page - 1) * limit).Take(limit).ToList();

            if (slice.Count == 0)
            {
                var empty = new HttpResponseMessage(HttpStatusCode.NoContent);
                empty.Headers.Add("X-Total-Count", Torrents.Count.ToString(CultureInfo.InvariantCulture));
                return Task.FromResult(empty);
            }

            var body = "[" + string.Join(",", slice.Select(t => t.GetRawText())) + "]";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.Add("X-Total-Count", Torrents.Count.ToString(CultureInfo.InvariantCulture));
            AfterPage?.Invoke(page);
            return Task.FromResult(response);
        }

        throw new InvalidOperationException("The sync should not call " + uri);
    }

    private static Task<HttpResponseMessage> Json(string body)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
}
