using System;
using System.ComponentModel;
using System.Security.Cryptography;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Validation;
using MediaBrowser.Model.Attributes;

namespace Emby.Plugin.RdZurg.Configuration;

/// <summary>
/// Everything the plugin needs to build a library out of a Real-Debrid account, as Emby's settings page shows it.
/// </summary>
public class PluginOptions : EditableOptionsBase
{
    /// <inheritdoc />
    public override string EditorTitle => "RD zurg";

    /// <inheritdoc />
    public override string EditorDescription =>
        "Your Real-Debrid library as two Emby libraries, without a mount. Run the \"Sync Real-Debrid library\" "
        + "scheduled task after saving.";

    /// <summary>Gets or sets the Real-Debrid API token.</summary>
    [DisplayName("API token")]
    [Description("The private token from real-debrid.com/apitoken.")]
    [IsPassword]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the library that holds movies.</summary>
    [DisplayName("Movie library")]
    [Description("Used when the library is first created. Rename it in Emby's library settings afterwards.")]
    public string MovieLibraryName { get; set; } = "Real-Debrid Movies";

    /// <summary>Gets or sets the name of the library that holds shows.</summary>
    [DisplayName("Show library")]
    [Description("Used when the library is first created. Rename it in Emby's library settings afterwards.")]
    public string ShowLibraryName { get; set; } = "Real-Debrid Shows";

    /// <summary>Gets or sets how many of the newest torrents to take. Zero means all of them.</summary>
    [DisplayName("Torrent limit")]
    [Description("0 lists the whole account. A positive limit imports the newest torrents and removes nothing.")]
    public int MaxTorrents { get; set; }

    /// <summary>Gets or sets a value indicating whether items are removed once their torrent is gone.</summary>
    [DisplayName("Remove vanished items")]
    [Description("Only after a complete listing of the whole account. Nothing on Real-Debrid is ever deleted.")]
    public bool RemoveVanishedItems { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether stored RAR archives are served through.</summary>
    [DisplayName("Look inside RAR archives")]
    [Description("Plays complete, unencrypted, stored video members of single-volume RAR archives.")]
    public bool UnwrapArchives { get; set; } = true;

    /// <summary>Gets or sets whether plain files redirect to the CDN instead of passing through the plugin.</summary>
    [DisplayName("Redirect plain files")]
    [Description("Emby still fetches the bytes itself; this only skips the plugin's own copy. Archives always pass through.")]
    [IsAdvanced]
    public bool RedirectDirectStreams { get; set; }

    /// <summary>Gets or sets the minimum gap between Real-Debrid calls, in milliseconds.</summary>
    [DisplayName("API interval (ms)")]
    [Description("At least 300 ms between Real-Debrid calls.")]
    [IsAdvanced]
    public int MinRequestIntervalMs { get; set; } = 300;

    /// <summary>Gets or sets the address Emby reaches its own stream route at, when the default cannot work.</summary>
    [DisplayName("Server address override")]
    [Description("Leave empty. Emby's own ffmpeg reads the .strm files and cannot resolve host names such as "
        + "localhost, so the default is this server's loopback address. Players never use it.")]
    [IsAdvanced]
    public string ServerUrlOverride { get; set; } = string.Empty;

    /// <summary>Gets or sets the private signing key for per-file playback capabilities.</summary>
    [Browsable(false)]
    public string StreamSecret { get; set; } = string.Empty;

    /// <summary>Gets or sets the Real-Debrid account id the published files were signed for.</summary>
    [Browsable(false)]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Creates a signing key.</summary>
    /// <returns>32 random bytes as hex.</returns>
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Reports whether the signing key holds 32 bytes of hex.</summary>
    /// <param name="secret">The key.</param>
    /// <returns><c>true</c> when it is usable.</returns>
    public static bool IsValidSecret(string? secret)
        => Streaming.StreamAccess.TryDecode32(secret, out _);

    /// <summary>Checks the settings the way the settings page does, for callers outside it.</summary>
    /// <returns>The first problem, or <c>null</c> when the settings are usable.</returns>
    public string? Problem()
    {
        if (string.IsNullOrWhiteSpace(MovieLibraryName) || string.IsNullOrWhiteSpace(ShowLibraryName)
            || string.Equals(MovieLibraryName.Trim(), ShowLibraryName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return "Movie and show libraries need distinct, nonempty names.";
        }

        if (MaxTorrents < 0)
        {
            return "The torrent limit cannot be negative.";
        }

        if (MinRequestIntervalMs < 300 || MinRequestIntervalMs > 60000)
        {
            return "The API interval must be between 300 and 60000 ms.";
        }

        if (!string.IsNullOrWhiteSpace(ServerUrlOverride)
            && (!Uri.TryCreate(ServerUrlOverride.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)))
        {
            return "The server address must be an absolute HTTP(S) URL without credentials, a query or a fragment.";
        }

        return null;
    }

    /// <inheritdoc />
    protected override void Validate(ValidationContext context)
    {
        var problem = Problem();
        if (problem is not null)
        {
            context.AddValidationError(problem);
        }
    }
}
