using Emby.Plugin.RdZurg.RealDebrid;
using Xunit;

namespace Emby.Plugin.RdZurg.Tests;

public class LinkKeyTests
{
    /// <summary>
    /// A Real-Debrid /d/ id is 13 characters of content key and, on about a third of responses, 3
    /// more that bind it to the account that minted it. The bare 13 are what keeps working, so they
    /// are what gets stored.
    /// </summary>
    [Theory]
    [InlineData("https://real-debrid.com/d/ZH4JR4PYJ6S2CPRY", "ZH4JR4PYJ6S2C")]
    [InlineData("https://real-debrid.com/d/ZH4JR4PYJ6S2C", "ZH4JR4PYJ6S2C")]
    [InlineData("ZH4JR4PYJ6S2CPRY", "ZH4JR4PYJ6S2C")]
    [InlineData("ZH4JR4PYJ6S2C", "ZH4JR4PYJ6S2C")]
    public void KeepsOnlyTheContentKey(string link, string expected)
    {
        Assert.Equal(expected, RealDebridClient.LinkKey(link));
    }

    [Fact]
    public void LeavesSomethingShorterAlone()
    {
        Assert.Equal("SHORT", RealDebridClient.LinkKey("https://real-debrid.com/d/SHORT"));
    }
}
