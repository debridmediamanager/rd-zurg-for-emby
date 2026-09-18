using System;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Logging;

namespace Emby.Plugin.RdZurg.Library;

/// <summary>
/// Creates the two libraries the tree is scanned into, and tells Emby when they changed.
/// </summary>
public sealed class LibraryBuilder
{
    /// <summary>The image fetcher that reads frames out of the video itself.</summary>
    /// <remarks>
    /// It is on by default, and here every frame it wants costs a Real-Debrid link and a download. Nothing else
    /// in a scan opens a stream, so this is the one fetcher the plugin removes.
    /// </remarks>
    public const string ImageCapture = "Image Capture";

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly ILibraryMonitor _libraryMonitor;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="LibraryBuilder"/> class.</summary>
    /// <param name="libraryManager">Emby's library manager.</param>
    /// <param name="providerManager">Emby's provider manager, which knows the default fetchers.</param>
    /// <param name="libraryMonitor">Emby's library monitor.</param>
    /// <param name="logger">Logger.</param>
    public LibraryBuilder(ILibraryManager libraryManager, IProviderManager providerManager, ILibraryMonitor libraryMonitor, ILogger logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _libraryMonitor = libraryMonitor;
        _logger = logger;
    }

    /// <summary>Creates the library for a directory, or leaves the existing one alone.</summary>
    /// <param name="name">The library's name, used only when it is created.</param>
    /// <param name="path">The directory it points at.</param>
    /// <param name="contentType">"movies" or "tvshows".</param>
    public void Ensure(string name, string path, string contentType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Directory.CreateDirectory(path);

        var folders = _libraryManager.GetVirtualFolders();
        if (folders.Any(v => v.Locations.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        if (folders.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("A library not owned by RD zurg is already called " + name + ".");
        }

        _libraryManager.AddVirtualFolder(name, contentType, Options(path, contentType), false);
        _logger.Info("RD zurg: created the {0} library at {1}", name, path);
    }

    /// <summary>Asks Emby to rescan one of the plugin's libraries.</summary>
    /// <param name="path">The library's directory.</param>
    /// <remarks>
    /// Emby refreshes that library alone, after its own delay. A full scan would walk every library on the
    /// server, which on a large install costs far more than the plugin's own tree.
    /// </remarks>
    public void ReportChanged(string path)
    {
        _libraryMonitor.ReportFileSystemChanged(path);
        _logger.Info("RD zurg: asked Emby to rescan {0}", path);
    }

    /// <summary>The options a library of the plugin's is created with.</summary>
    /// <param name="path">The directory.</param>
    /// <param name="contentType">"movies" or "tvshows".</param>
    /// <returns>The options.</returns>
    public LibraryOptions Options(string path, string contentType)
    {
        // Built from scratch, LibraryOptions has no TypeOptions at all, which means no metadata fetchers and a
        // library that never matches anything. Emby's own defaults are the starting point.
        var options = _providerManager.GetDefaultLibraryOptions(contentType) ?? new LibraryOptions();

        options.PathInfos = new[] { new MediaPathInfo { Path = path } };
        options.ContentType = contentType;

        // Nothing here may open a stream, and nothing may write into the tree.
        options.EnableRealtimeMonitor = false;
        options.SaveLocalMetadata = false;
        options.SaveSubtitlesWithMedia = false;
        options.SaveLyricsWithMedia = false;
        options.EnableChapterImageExtraction = false;
        options.ExtractChapterImagesDuringLibraryScan = false;
        options.EnableMarkerDetection = false;
        options.EnableMarkerDetectionDuringLibraryScan = false;
        options.ThumbnailImagesIntervalSeconds = -1;
        options.AutoGenerateChapters = false;

        // Versions are what this plugin's folders say they are, never what metadata guesses.
        options.EnableMultiVersionByFiles = true;
        options.EnableMultiVersionByMetadata = false;

        options.TypeOptions = (options.TypeOptions ?? Array.Empty<TypeOptions>())
            .Select(type => new TypeOptions
            {
                Type = type.Type,
                MetadataFetchers = type.MetadataFetchers,
                MetadataFetcherOrder = type.MetadataFetcherOrder,
                ImageFetchers = (type.ImageFetchers ?? Array.Empty<string>())
                    .Where(f => !string.Equals(f, ImageCapture, StringComparison.OrdinalIgnoreCase))
                    .ToArray(),
                ImageFetcherOrder = type.ImageFetcherOrder,
                ImageOptions = type.ImageOptions,
            })
            .ToArray();

        return options;
    }
}
