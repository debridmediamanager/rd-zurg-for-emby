using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugin.RdZurg.RealDebrid;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>Brings the libraries in line with the Real-Debrid account.</summary>
public class RdSyncTask : IScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly IServerApplicationHost _host;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="RdSyncTask"/> class.</summary>
    /// <param name="libraryManager">Emby's library manager.</param>
    /// <param name="providerManager">Emby's provider manager.</param>
    /// <param name="libraryMonitor">Emby's library monitor.</param>
    /// <param name="host">Emby's application host, which knows its own address.</param>
    /// <param name="logManager">Emby's log manager.</param>
    public RdSyncTask(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        ILibraryMonitor libraryMonitor,
        IServerApplicationHost host,
        ILogManager logManager)
    {
        ArgumentNullException.ThrowIfNull(logManager);
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _libraryMonitor = libraryMonitor;
        _host = host;
        _logger = logManager.GetLogger(Plugin.PluginName);
    }

    /// <inheritdoc />
    public string Name => "Sync Real-Debrid library";

    /// <inheritdoc />
    public string Key => "RdZurgSync";

    /// <inheritdoc />
    public string Description => "Publishes the Real-Debrid account as Emby libraries.";

    /// <inheritdoc />
    public string Category => Plugin.PluginName;

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => new[]
    {
        new TaskTriggerInfo { Type = TaskTriggerInfo.TriggerInterval, IntervalTicks = TimeSpan.FromHours(6).Ticks },
    };

    /// <inheritdoc />
    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var plugin = Plugin.Instance ?? throw new InvalidOperationException("The plugin is not loaded.");
        var options = plugin.Options;

        if (options.Problem() is string problem)
        {
            throw new InvalidOperationException(problem);
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("No Real-Debrid API token is configured.");
        }

        var root = Path.Combine(plugin.DataPath, "rd-zurg");
        var tree = new StrmTree(root, _logger);
        tree.Ensure();

        var builder = new LibraryBuilder(_libraryManager, _providerManager, _libraryMonitor, _logger);
        builder.Ensure(options.MovieLibraryName, Path.Combine(root, StrmPaths.Movies), "movies");
        builder.Ensure(options.ShowLibraryName, Path.Combine(root, StrmPaths.Shows), "tvshows");

        var client = new RealDebridClient(Plugin.Http, options.ApiKey, options.MinRequestIntervalMs);
        var accountId = await client.GetAccountIdAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(accountId, options.AccountId, StringComparison.Ordinal))
        {
            // Every published URL is signed for one account, so switching accounts rewrites them all.
            _logger.Info("RD zurg: publishing for Real-Debrid account {0}", accountId);
            options.AccountId = accountId;
            plugin.Save(options);
        }

        var ledgerPath = Path.Combine(plugin.DataPath, "rd-zurg.ledger.json");
        var ledger = Ledger.Load(ledgerPath);
        var sync = new LibrarySync(client, tree, ledger, _logger);

        if (ledger.Entries.Count == 0)
        {
            var recovered = sync.RecoverLedger();
            if (recovered > 0)
            {
                _logger.Info("RD zurg: recovered {0} published files from the library tree", recovered);
            }
        }

        var baseUrl = BaseUrl(options.ServerUrlOverride);
        var result = await sync.RunAsync(options, baseUrl, accountId, progress, cancellationToken).ConfigureAwait(false);
        ledger.Save(ledgerPath);
        _logger.Info("RD zurg: {0}", result.ToString());

        if (result.FilesWritten + result.FilesRewritten + result.FilesDeleted > 0)
        {
            builder.ReportChanged(Path.Combine(root, StrmPaths.Movies));
            builder.ReportChanged(Path.Combine(root, StrmPaths.Shows));
        }
    }

    private string BaseUrl(string? over)
    {
        if (!string.IsNullOrWhiteSpace(over))
        {
            return over.Trim().TrimEnd('/');
        }

        // Only Emby itself fetches these URLs, and its own ffmpeg build cannot resolve host names such as
        // localhost, so the loopback address is both the safest and the only reliable choice.
        return _host.GetLocalApiUrl(IPAddress.Loopback).TrimEnd('/');
    }
}
