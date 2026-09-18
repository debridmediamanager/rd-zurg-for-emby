using System;
using System.Linq;
using Emby.Plugin.RdZurg.Archive;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class RarReaderTests
{
    /// <summary>
    /// The first 200 bytes of a real archive as Real-Debrid served it on 2026-09-08, for the link
    /// whose <c>/torrents/info</c> entry claimed a plain <c>.mkv</c> of 719,305,363 bytes. The
    /// archive is 719,305,511 bytes, so the wrapper costs 148: a 141 byte header and RAR4's 7 byte
    /// end marker.
    /// </summary>
    private const string RealDebridRar4Header =
        ""
        + "526172211a0700cf907300000d00000000000000631f742090790093badf2a93"
        + "badf2a03872693b3c0b3fa5c14305400a4810000426162792e4c6f6f6e65792e"
        + "54756e65732e5330314532332e536861646f772e6f662e612e446f7562742e31"
        + "303830702e484d41582e5745422e444c2e4444352e312e482e3236342d706c61"
        + "795745422e6d6b7600f06267811a45dfa3a34286810142f7810142f2810442f3"
        + "81084282886d6174726f736b61428781044285810218538067010000002adfba"
        + "5f114d9b74cf4dbb";

    private static byte[] Fixture() => Convert.FromHexString(RealDebridRar4Header);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void RejectsSplitOrEncryptedMembers(int flag)
    {
        var bytes = Fixture();
        bytes[23] |= (byte)flag;
        Assert.False(RarReader.TryGetPrimaryEntry(bytes, out _));
    }

    [Fact]
    public void NeverReadsANameBeyondItsDeclaredHeader()
    {
        var bytes = Fixture();
        bytes[25] = 32;
        bytes[26] = 0;
        Assert.False(RarReader.TryGetPrimaryEntry(bytes, out _));
    }

    [Fact]
    public void RecognisesTheArchiveRealDebridActuallyServes()
    {
        Assert.True(RarReader.LooksLikeRar(Fixture()));
    }

    [Fact]
    public void FindsTheMemberAtTheSizeTheFileListPromised()
    {
        Assert.True(RarReader.TryGetPrimaryEntry(Fixture(), out var entry));

        // The size the torrent's file list reported for the "mkv" that is really a rar.
        Assert.Equal(719_305_363, entry.Length);
        Assert.Equal(141, entry.DataOffset);
        Assert.True(entry.IsStored);
        Assert.StartsWith("Baby.Looney.Tunes.S01E23", entry.Name, StringComparison.Ordinal);
        Assert.EndsWith(".mkv", entry.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void MapsAMemberReadOntoAnArchiveRead()
    {
        Assert.True(RarReader.TryGetPrimaryEntry(Fixture(), out var entry));

        Assert.Equal(141, entry.ToArchiveOffset(0));
        Assert.Equal(141 + 1_000_000, entry.ToArchiveOffset(1_000_000));
    }

    [Fact]
    public void IgnoresBytesThatAreNotAnArchive()
    {
        var notRar = new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x00, 0x00, 0x00 };

        Assert.False(RarReader.LooksLikeRar(notRar));
        Assert.Empty(RarReader.ListEntries(notRar));
        Assert.False(RarReader.TryGetPrimaryEntry(notRar, out _));
    }

    [Fact]
    public void SurvivesATruncatedHeader()
    {
        var truncated = Fixture().Take(30).ToArray();

        // Half a header is not a member. What matters is that it does not throw.
        Assert.DoesNotContain(RarReader.ListEntries(truncated), e => e.IsStored);
    }
}
