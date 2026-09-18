using System;
using System.Collections.Generic;
using System.Text;
using Emby.Plugin.RdZurg.Archive;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class Rar5Tests
{
    [Fact]
    public void ReadsAStoredVideoMember()
    {
        var bytes = Archive(2, 0);
        Assert.True(RarReader.TryGetPrimaryEntry(bytes, out var entry));
        Assert.Equal("sample.mkv", entry.Name);
        Assert.Equal(1000, entry.Length);
        Assert.Equal(bytes.Length, entry.DataOffset);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(18, 0)]
    [InlineData(2, 128)]
    public void RejectsSplitAndCompressedMembers(int flags, int method)
        => Assert.False(RarReader.TryGetPrimaryEntry(Archive(flags, method), out _));

    [Fact]
    public void TruncationNeverInventsAMember()
    {
        var bytes = Archive(2, 0);
        for (var i = 0; i < bytes.Length; i++)
            Assert.False(RarReader.TryGetPrimaryEntry(bytes.AsSpan(0, i), out _));
    }

    [Fact]
    public void OverflowingHeaderSizeIsRejected()
    {
        var bytes = Convert.FromHexString("526172211A07010000000000FFFFFFFFFFFFFFFFFF7F");
        Assert.Empty(RarReader.ListEntries(bytes));
    }

    private static byte[] Archive(int flags, int compression)
    {
        var header = new List<byte>();
        void Number(long n) { while (n >= 128) { header.Add((byte)(n | 128)); n >>= 7; } header.Add((byte)n); }
        Number(2); Number(flags); Number(1000); // type, block flags, packed size
        Number(0); Number(1000); Number(0); // file flags, unpacked size, attributes
        Number(compression); Number(1); // method, host OS
        var name = Encoding.UTF8.GetBytes("sample.mkv");
        Number(name.Length); header.AddRange(name);
        var result = new List<byte>(Convert.FromHexString("526172211A07010000000000")) { (byte)header.Count };
        result.AddRange(header);
        return result.ToArray();
    }
}
