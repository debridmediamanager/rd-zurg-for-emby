// Derived from MediaBrowser/Emby.Naming (commit c2a5776, 2019-05-16), MIT licensed; see LICENSE-Emby.Naming.md.
// Kept close to upstream on purpose. The plugin's own changes to the expressions live in Library/ReleaseNames.cs.
// Upstream also resolves stubs, flags and 3D formats; none of that feeds a release's identity, so it is left out.
using System;
using System.IO;
using System.Linq;

namespace Emby.Plugin.RdZurg.Naming;

internal sealed class EpisodeResolver
{
    private readonly NamingOptions _options;

    public EpisodeResolver(NamingOptions options)
    {
        _options = options;
    }

    public EpisodePathParserResult? Resolve(string path, bool isDirectory, bool? isNamed = null, bool? isOptimistic = null, bool? supportsAbsoluteNumbers = null, bool fillExtendedInfo = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!isDirectory)
        {
            var extension = Path.GetExtension(path) ?? string.Empty;
            if (!_options.VideoFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return new EpisodePathParser(_options).Parse(path, isDirectory, isNamed, isOptimistic, supportsAbsoluteNumbers, fillExtendedInfo);
    }
}
