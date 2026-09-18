using Emby.Plugin.RdZurg.Streaming;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class ByteRangeTests
{
    [Theory]
    [InlineData("bytes=0-899", 0, 899)]
    [InlineData("bytes=500-", 500, 999)]
    [InlineData("bytes=-100", 900, 999)]
    [InlineData("bytes=0-99999", 0, 999)] // past the end is clamped, not refused
    [InlineData("BYTES=10-20", 10, 20)]
    public void ReadsTheFormsPlayersSend(string header, long from, long to)
    {
        Assert.True(ByteRange.TryParse(header, 1000, out var range));
        Assert.Equal(from, range!.Value.From);
        Assert.Equal(to, range.Value.To);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("items=0-10")]
    [InlineData("bytes=abc-def")]
    [InlineData("bytes=1000-1010")]
    [InlineData("bytes=500-400")]
    [InlineData("bytes=0-10,20-30")]
    public void RefusesWhatItCannotSatisfy(string? header)
    {
        Assert.False(ByteRange.TryParse(header, 1000, out _));
    }

    [Fact]
    public void CountsInclusively()
    {
        Assert.True(ByteRange.TryParse("bytes=0-0", 1000, out var single));
        Assert.Equal(1, single!.Value.Length);

        Assert.True(ByteRange.TryParse("bytes=0-999", 1000, out var whole));
        Assert.Equal(1000, whole!.Value.Length);
    }

    [Fact]
    public void RefusesAnythingWhenThereIsNoLength()
    {
        Assert.False(ByteRange.TryParse("bytes=0-10", 0, out _));
    }
}
