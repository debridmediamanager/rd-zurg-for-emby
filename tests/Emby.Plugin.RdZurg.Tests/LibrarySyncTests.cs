using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.Library;
using Emby.Plugin.RdZurg.RealDebrid;
using Emby.Plugin.RdZurg.Streaming;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

/// <summary>
/// The sync, driven by a real Real-Debrid account captured from the test account on 2026-09-12: 123 torrents,
/// with the duplicate releases, the season packs and the 109 copies of one film that the account really holds.
/// </summary>
public sealed class LibrarySyncTests : IDisposable
{
    private const string Secret = "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";
    private const string BaseUrl = "http://127.0.0.1:8096";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rd-zurg-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public async Task PublishesTheAccountAsAFileTree()
    {
        var (sync, account, options, tree) = Sync();
        var result = await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        // What this account holds: 123 torrents, 16 releases present twice, and 109 copies of The Matrix, of
        // which Emby can group 8.
        Assert.Equal(123, result.TorrentsSeen);
        Assert.Equal(61, result.FilesWritten);
        Assert.Equal(45, result.Capped);

        // Every copy in this account shares its original's link key, so none of them reaches the size check.
        Assert.Equal(0, result.Duplicates);
        Assert.Equal(0, result.FilesRewritten);

        var published = tree.Published();
        Assert.Equal(result.FilesWritten, published.Count);

        foreach (var (path, url) in published)
        {
            var parts = path.Split('/');
            Assert.True(parts[0] is StrmPaths.Movies or StrmPaths.Shows, path);
            Assert.EndsWith(".strm", path, StringComparison.Ordinal);
            Assert.StartsWith(BaseUrl + "/RdZurg/Stream/", url, StringComparison.Ordinal);
            Assert.NotNull(StreamUrls.KeyOf(url));

            // Emby only groups a folder's files as versions of one item when each starts with the folder's name.
            if (parts[0] == StrmPaths.Movies)
            {
                Assert.StartsWith(parts[1] + " - ", parts[2], StringComparison.Ordinal);
            }
            else
            {
                Assert.Matches(@"^Season [0-9]{2}$", parts[2]);
                Assert.StartsWith(parts[1] + " - S", parts[3], StringComparison.Ordinal);
            }

            foreach (var component in parts)
            {
                Assert.DoesNotMatch(@"[<>:""\\|?*]", component);
                Assert.False(component.StartsWith('.') || component.EndsWith('.') || component.EndsWith(' '), component);
                Assert.True(System.Text.Encoding.UTF8.GetByteCount(component) <= 255, component);
            }
        }
    }

    [Fact]
    public async Task NoFolderHoldsMoreVersionsThanEmbyGroups()
    {
        var (sync, account, options, tree) = Sync();
        var result = await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        var folders = tree.Published().Keys
            .Where(p => p.StartsWith(StrmPaths.Movies + "/", StringComparison.Ordinal))
            .GroupBy(p => p[..p.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.All(folders, f => Assert.True(f.Count() <= StrmPaths.MaxVersions, $"{f.Key} holds {f.Count()} versions"));

        // The account really does hold 109 releases of one film, so the cap has to have done something.
        Assert.True(result.Capped > 0, "nothing was capped");
        Assert.Contains(folders, f => f.Count() == StrmPaths.MaxVersions);
    }

    [Fact]
    public async Task ReleasesWithoutAYearNeverShareAFolder()
    {
        var (sync, account, options, tree) = Sync();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        var yearless = tree.Published().Keys
            .Where(p => p.StartsWith(StrmPaths.Movies + "/", StringComparison.Ordinal))
            .Select(p => p.Split('/')[1])
            .Where(folder => !System.Text.RegularExpressions.Regex.IsMatch(folder, @"\((?:19|20)[0-9]{2}\)$"))
            .ToList();

        Assert.All(yearless, folder => Assert.Matches(@"\[[A-Z0-9]{13}\]$", folder));
        Assert.Equal(yearless.Count, yearless.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task ASecondPassWritesNothingAndAsksTheProviderNothing()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var times = Times(tree);
        var detailCalls = account.DetailCalls;

        var (second, _) = Continue(account, path);
        var result = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        Assert.Equal(0, result.FilesWritten);
        Assert.Equal(0, result.FilesRewritten);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(times, Times(tree));

        // Only the four torrents whose file list and links disagree are asked about again; the capture recorded
        // exactly which ones those are.
        Assert.Equal(119, result.TorrentsAlreadyKnown);
        Assert.Equal(4, result.DetailCalls);
        Assert.Equal(
            FakeAccount.PartialTorrents("rd-library-1.0.2.0.json").OrderBy(id => id, StringComparer.Ordinal),
            account.Detailed.Skip(detailCalls).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ALimitedListingRemovesNothing()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var before = tree.Published();

        options.MaxTorrents = 10;
        var (second, _) = Continue(account, path);
        var result = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(before.Count, tree.Published().Count);
    }

    [Fact]
    public async Task AProviderRefusalFailsBeforeAnythingIsRemoved()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var before = tree.Published();

        account.ListingFailure = HttpStatusCode.InternalServerError;
        var (second, _) = Continue(account, path);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None));

        Assert.Equal(before.Count, tree.Published().Count);
    }

    [Fact]
    public async Task AnAccountThatSuddenlyEmptiesDoesNotEmptyTheLibrary()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var before = tree.Published();

        account.Torrents.Clear();
        var (second, _) = Continue(account, path);
        var result = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        Assert.True(result.CleanupRefused);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(before.Count, tree.Published().Count);

        // With the override the same pass does clean up, which is what the setting is for.
        options.AllowLargeCleanup = true;
        var (third, _) = Continue(account, path);
        var cleaned = await third.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        Assert.Equal(before.Count, cleaned.FilesDeleted);
        Assert.Empty(tree.Published());
    }

    [Fact]
    public async Task AVanishedTorrentTakesOnlyItsOwnFiles()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var before = tree.Published();

        // A torrent whose release is published exactly once: removing it must remove exactly its own files.
        var keys = before.Values.Select(StreamUrls.KeyOf).ToHashSet(StringComparer.Ordinal);
        var victim = account.Torrents.First(t => t.GetProperty("links").EnumerateArray()
            .Select(l => RealDebridClient.LinkKey(l.GetString()!)).Count(keys.Contains) == 1);
        var victimKeys = victim.GetProperty("links").EnumerateArray().Select(l => RealDebridClient.LinkKey(l.GetString()!)).ToHashSet(StringComparer.Ordinal);
        account.Remove(t => FakeAccount.Id(t) == FakeAccount.Id(victim));

        var (second, _) = Continue(account, path);
        var result = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        var after = tree.Published();
        var gone = before.Keys.Except(after.Keys).ToList();
        Assert.Equal(result.FilesDeleted, gone.Count);
        Assert.All(gone, p => Assert.Contains(StreamUrls.KeyOf(before[p]), victimKeys));
        Assert.Equal(before.Count - gone.Count, after.Count);
    }

    [Fact]
    public async Task AnotherAccountRewritesEveryFileExactlyOnce()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        var first = await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);

        account.AccountId = "7654321";
        var (second, _) = Continue(account, path);
        var result = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);

        Assert.Equal(first.FilesWritten, result.FilesRewritten);
        Assert.Equal(0, result.FilesWritten);
        Assert.Equal(0, result.FilesDeleted);

        var (third, _) = Continue(account, path);
        var again = await third.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        Assert.Equal(0, again.FilesRewritten);
    }

    [Fact]
    public async Task FilesThePluginDidNotWriteAreLeftAlone()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);

        var foreign = Path.Combine(tree.Root, StrmPaths.Movies, "Someone Else (2001)", "Someone Else (2001).strm");
        Directory.CreateDirectory(Path.GetDirectoryName(foreign)!);
        File.WriteAllText(foreign, "http://example.test/not-ours.mkv");
        var note = Path.Combine(tree.Root, StrmPaths.Movies, "notes.txt");
        File.WriteAllText(note, "keep me");

        account.Torrents.Clear();
        options.AllowLargeCleanup = true;
        var (second, _) = Continue(account, path);
        await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        Assert.True(File.Exists(foreign), "a .strm the plugin did not write was deleted");
        Assert.True(File.Exists(note));
    }

    [Fact]
    public async Task TheLedgerCanBeRebuiltFromTheTreeAlone()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);
        var published = tree.Published();

        File.Delete(path);
        var rebuilt = Ledger.Load(path);
        var recovering = new LibrarySync(new RealDebridClient(new HttpClient(account, false), "token", 0), tree, rebuilt, new FakeLogger());
        var recovered = recovering.RecoverLedger();

        Assert.Equal(published.Count, recovered);
        Assert.All(published, p => Assert.Equal(StreamUrls.KeyOf(p.Value), rebuilt.AtPath(p.Key)?.Key));

        // And a pass on the rebuilt ledger changes nothing on disk.
        var result = await recovering.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        Assert.Equal(0, result.FilesWritten);
        Assert.Equal(0, result.FilesDeleted);
    }

    /// <summary>
    /// The account holds 16 releases twice, byte for byte, and Real-Debrid gives both copies the same link key:
    /// the second torrent is recognised by its key alone and costs nothing, and the published file survives as
    /// long as either torrent does.
    /// </summary>
    [Fact]
    public async Task ACopyOfAReleaseSharesItsLinkAndCostsNothing()
    {
        var (sync, account, options, tree, ledger, path) = Full();
        account.Keep("4C4NNIPL7CNSZ", "UEQNG3CECSE3B");
        var result = await sync.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);
        ledger.Save(path);

        Assert.Equal(2, result.TorrentsSeen);
        Assert.Single(tree.Published());
        var published = ledger.Published.Single();

        // Dropping one of the two leaves the link live, so the file stays exactly where it was.
        account.Remove(t => FakeAccount.Id(t) == "4C4NNIPL7CNSZ");
        var (second, _) = Continue(account, path);
        var after = await second.RunAsync(options, BaseUrl, account.AccountId, null, CancellationToken.None);

        Assert.Equal(0, after.FilesDeleted);
        Assert.Equal(0, after.FilesRewritten);
        Assert.Equal(published.Key, StreamUrls.KeyOf(tree.Read(published.Path)));
    }

    private (LibrarySync Sync, FakeAccount Account, PluginOptions Options, StrmTree Tree) Sync()
    {
        var (sync, account, options, tree, _, _) = Full();
        return (sync, account, options, tree);
    }

    private (LibrarySync Sync, FakeAccount Account, PluginOptions Options, StrmTree Tree, Ledger Ledger, string LedgerPath) Full()
    {
        var account = new FakeAccount("rd-library-1.0.2.0.json");
        var options = new PluginOptions { ApiKey = "token", StreamSecret = Secret, AccountId = account.AccountId };
        var tree = new StrmTree(Path.Combine(_root, "rd-zurg"), new FakeLogger());
        tree.Ensure();
        var ledgerPath = Path.Combine(_root, "rd-zurg.ledger.json");
        var ledger = Ledger.Load(ledgerPath);
        var sync = new LibrarySync(new RealDebridClient(new HttpClient(account, false), "token", 0), tree, ledger, new FakeLogger());
        return (sync, account, options, tree, ledger, ledgerPath);
    }

    private (LibrarySync Sync, Ledger Ledger) Continue(FakeAccount account, string ledgerPath)
    {
        var ledger = Ledger.Load(ledgerPath);
        var tree = new StrmTree(Path.Combine(_root, "rd-zurg"), new FakeLogger());
        var sync = new LibrarySync(new RealDebridClient(new HttpClient(account, false), "token", 0), tree, ledger, new FakeLogger());
        return (sync, ledger);
    }

    private static Dictionary<string, DateTime> Times(StrmTree tree)
        => tree.Published().Keys.ToDictionary(
            p => p,
            p => File.GetLastWriteTimeUtc(Path.Combine(tree.Root, p.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.OrdinalIgnoreCase);
}
