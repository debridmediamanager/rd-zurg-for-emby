using System;
using System.IO;
using System.Net;
using System.Net.Http;
using Emby.Plugin.RdZurg.Configuration;
using Emby.Plugin.RdZurg.Streaming;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg;

/// <summary>
/// Serves a Real-Debrid account as two Emby libraries, with no mount and no second service.
/// </summary>
public class Plugin : BasePluginSimpleUI<PluginOptions>, IHasThumbImage
{
    /// <summary>
    /// The plugin's name. Emby names the settings file after it, so it never changes: a rename would start from
    /// empty settings and a new signing key.
    /// </summary>
    public const string PluginName = "RD zurg";

    // One client for the plugin's lifetime. Redirects are followed for the CDN; nothing decompresses, because a
    // byte range of a compressed body is not a byte range of the file.
    private static readonly HttpClient _http = new(new SocketsHttpHandler
    {
        AutomaticDecompression = DecompressionMethods.None,
        AllowAutoRedirect = true,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    /// <summary>Initializes a new instance of the <see cref="Plugin"/> class.</summary>
    /// <param name="applicationHost">Emby's application host.</param>
    /// <param name="logManager">Emby's log manager.</param>
    /// <param name="applicationPaths">Emby's paths.</param>
    public Plugin(IApplicationHost applicationHost, ILogManager logManager, IApplicationPaths applicationPaths)
        : base(applicationHost)
    {
        ArgumentNullException.ThrowIfNull(logManager);
        ArgumentNullException.ThrowIfNull(applicationPaths);

        Logger = logManager.GetLogger(PluginName);
        DataPath = applicationPaths.DataPath;

        // The signing key is minted once and kept: every published .strm file carries a signature made with it.
        var options = GetOptions();
        if (!PluginOptions.IsValidSecret(options.StreamSecret))
        {
            options.StreamSecret = PluginOptions.NewSecret();
            SaveOptions(options);
        }

        Resolver = new LinkResolver(_http, () => Instance?.Options, Logger);
        Responder = new StreamResponder(Resolver, _http, () => Instance?.Options, Logger);
        Instance = this;
    }

    /// <summary>Gets the running instance, for the parts of Emby that hand out no reference.</summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>Gets the plugin's logger.</summary>
    public ILogger Logger { get; }

    /// <summary>Gets Emby's data directory, under which the plugin keeps its library tree.</summary>
    public string DataPath { get; }

    /// <summary>Gets the shared HTTP client.</summary>
    public static HttpClient Http => _http;

    /// <summary>Gets the plugin's one link resolver.</summary>
    public LinkResolver Resolver { get; }

    /// <summary>Gets the plugin's one stream responder.</summary>
    public StreamResponder Responder { get; }

    /// <summary>Gets the current settings.</summary>
    public PluginOptions Options => GetOptions();

    /// <inheritdoc />
    public override string Name => PluginName;

    /// <inheritdoc />
    public override string Description => "Your Real-Debrid library in Emby, without a mount.";

    /// <inheritdoc />
    public override Guid Id => new("43175d8f-3984-445c-a4cb-e4d2e1aa0fce");

    /// <inheritdoc />
    public ImageFormat ThumbImageFormat => ImageFormat.Png;

    /// <summary>Saves settings the plugin changed itself, such as the account id.</summary>
    /// <param name="options">The settings.</param>
    public void Save(PluginOptions options) => SaveOptions(options);

    /// <inheritdoc />
    public Stream GetThumbImage()
        => GetType().Assembly.GetManifestResourceStream("Emby.Plugin.RdZurg.thumb.png")
            ?? throw new InvalidOperationException("The thumbnail is missing from the assembly.");

    /// <inheritdoc />
    protected override void OnOptionsSaved(PluginOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Logger.Info("RD zurg: settings saved");
    }
}
