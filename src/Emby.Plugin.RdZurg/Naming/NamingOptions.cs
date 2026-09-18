// Derived from MediaBrowser/Emby.Naming (commit c2a5776, 2019-05-16), MIT licensed; see LICENSE-Emby.Naming.md.
// Only the tables the episode resolver reads are kept, verbatim. The plugin's own changes to
// them are applied in Library/ReleaseNames.cs, not here.
using System;
using System.Linq;

namespace Emby.Plugin.RdZurg.Naming;

internal sealed class NamingOptions
{
    public NamingOptions()
    {
        var extensions = new[]
        {
            ".m4v",
            ".3gp",
            ".nsv",
            ".ts",
            ".ty",
            ".strm",
            ".rm",
            ".rmvb",
            ".ifo",
            ".mov",
            ".qt",
            ".divx",
            ".xvid",
            ".bivx",
            ".vob",
            ".nrg",
            ".img",
            ".iso",
            ".pva",
            ".wmv",
            ".asf",
            ".asx",
            ".ogm",
            ".m2v",
            ".avi",
            ".bin",
            ".dvr-ms",
            ".mpg",
            ".mpeg",
            ".mp4",
            ".mkv",
            ".avc",
            ".vp3",
            ".svq3",
            ".nuv",
            ".viv",
            ".dv",
            ".fli",
            ".flv",
            ".001",
            ".tp"
        };

        EpisodeExpressions = new[]
        {
            // *** Begin Kodi Standard Naming
            // <!-- foo.s01.e01, foo.s01_e01, S01E02 foo, S01 - E02 -->
            new EpisodeExpression(@".*(\\|\/)(?<seriesname>((?![Ss]([0-9]+)[][ ._-]*[Ee]([0-9]+))[^\\\/])*)?[Ss](?<seasonnumber>[0-9]+)[][ ._-]*[Ee](?<epnumber>[0-9]+)([^\\/]*)$")
            {
                IsNamed = true
            }, 
            // <!-- foo.ep01, foo.EP_01 -->
            new EpisodeExpression(@"[\._ -]()[Ee][Pp]_?([0-9]+)([^\\/]*)$"),
            new EpisodeExpression("([0-9]{4})[\\.-]([0-9]{2})[\\.-]([0-9]{2})", true)
            {
                DateTimeFormats = new []
                {
                    "yyyy.MM.dd",
                    "yyyy-MM-dd",
                    "yyyy_MM_dd"
                }
            },
            new EpisodeExpression("([0-9]{2})[\\.-]([0-9]{2})[\\.-]([0-9]{4})", true)
            {
                DateTimeFormats = new []
                {
                    "dd.MM.yyyy",
                    "dd-MM-yyyy",
                    "dd_MM_yyyy"
                }
            },

            new EpisodeExpression("[\\\\/\\._ \\[\\(-]([0-9]+)x([0-9]+(?:(?:[a-i]|\\.[1-9])(?![0-9]))?)([^\\\\/]*)$")
            {
                SupportsAbsoluteEpisodeNumbers = true
            },
            new EpisodeExpression(@"[\\\\/\\._ -](?<seriesname>(?![0-9]+[0-9][0-9])([^\\\/])*)[\\\\/\\._ -](?<seasonnumber>[0-9]+)(?<epnumber>[0-9][0-9](?:(?:[a-i]|\\.[1-9])(?![0-9]))?)([\\._ -][^\\\\/]*)$")
            {
                IsOptimistic = true,
                IsNamed = true,
                SupportsAbsoluteEpisodeNumbers = false
            },
            new EpisodeExpression("[\\/._ -]p(?:ar)?t[_. -]()([ivx]+|[0-9]+)([._ -][^\\/]*)$")
            {
                SupportsAbsoluteEpisodeNumbers = true
            },

            // *** End Kodi Standard Naming

            new EpisodeExpression(@".*(\\|\/)[sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3})[^\\\/]*$")
            {
                IsNamed = true
            },

            new EpisodeExpression(@".*(\\|\/)[sS](?<seasonnumber>\d{1,4})[x,X]?[eE](?<epnumber>\d{1,3})[^\\\/]*$")
            {
                IsNamed = true
            },

            new EpisodeExpression(@".*(\\|\/)(?<seriesname>((?![sS]?\d{1,4}[xX]\d{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3}))[^\\\/]*$")
            {
                IsNamed = true
            },

            new EpisodeExpression(@".*(\\|\/)(?<seriesname>[^\\\/]*)[sS](?<seasonnumber>\d{1,4})[xX\.]?[eE](?<epnumber>\d{1,3})[^\\\/]*$")
            {
                IsNamed = true
            },

            // "01.avi"
            new EpisodeExpression(@".*[\\\/](?<epnumber>\d{1,3})(-(?<endingepnumber>\d{2,3}))*\.\w+$")
            {
                IsOptimistic = true,
                IsNamed = true
            },

            // "1-12 episode title"
            new EpisodeExpression(@"([0-9]+)-([0-9]+)")
            {
            },

            // "01 - blah.avi", "01-blah.avi"
            new EpisodeExpression(@".*(\\|\/)(?<epnumber>\d{1,3})(-(?<endingepnumber>\d{2,3}))*\s?-\s?[^\\\/]*$")
            {
                IsOptimistic = true,
                IsNamed = true
            },

            // "01.blah.avi"
            new EpisodeExpression(@".*(\\|\/)(?<epnumber>\d{1,3})(-(?<endingepnumber>\d{2,3}))*\.[^\\\/]+$")
            {
                IsOptimistic = true,
                IsNamed = true
            },

            // "blah - 01.avi", "blah 2 - 01.avi", "blah - 01 blah.avi", "blah 2 - 01 blah", "blah - 01 - blah.avi", "blah 2 - 01 - blah"
            new EpisodeExpression(@".*[\\\/][^\\\/]* - (?<epnumber>\d{1,3})(-(?<endingepnumber>\d{2,3}))*[^\\\/]*$")
            {
                IsOptimistic = true,
                IsNamed = true
            },

            // "01 episode title.avi"
            new EpisodeExpression(@"[Ss]eason[\._ ](?<seasonnumber>[0-9]+)[\\\/](?<epnumber>\d{1,3})([^\\\/]*)$")
            {
                IsOptimistic = true,
                IsNamed = true
            },
            // "Episode 16", "Episode 16 - Title"
            new EpisodeExpression(@".*[\\\/][^\\\/]* (?<epnumber>\d{1,3})(-(?<endingepnumber>\d{2,3}))*[^\\\/]*$")
            {
                IsOptimistic = true,
                IsNamed = true
            }
        };

        MultipleEpisodeExpressions = new string[]
        {
            @".*(\\|\/)[sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3})((-| - )\d{1,4}[eExX](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)[sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3})((-| - )\d{1,4}[xX][eE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)[sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3})((-| - )?[xXeE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)[sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3})(-[xE]?[eE]?(?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>((?![sS]?\d{1,4}[xX]\d{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3}))((-| - )\d{1,4}[xXeE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>((?![sS]?\d{1,4}[xX]\d{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3}))((-| - )\d{1,4}[xX][eE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>((?![sS]?\d{1,4}[xX]\d{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3}))((-| - )?[xXeE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>((?![sS]?\d{1,4}[xX]\d{1,3})[^\\\/])*)?([sS]?(?<seasonnumber>\d{1,4})[xX](?<epnumber>\d{1,3}))(-[xX]?[eE]?(?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>[^\\\/]*)[sS](?<seasonnumber>\d{1,4})[xX\.]?[eE](?<epnumber>\d{1,3})((-| - )?[xXeE](?<endingepnumber>\d{1,3}))+[^\\\/]*$",
            @".*(\\|\/)(?<seriesname>[^\\\/]*)[sS](?<seasonnumber>\d{1,4})[xX\.]?[eE](?<epnumber>\d{1,3})(-[xX]?[eE]?(?<endingepnumber>\d{1,3}))+[^\\\/]*$"

        }.Select(i => new EpisodeExpression(i)
        {
            IsNamed = true

        }).ToArray();

        VideoFileExtensions = extensions
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    }

    public string[] VideoFileExtensions { get; set; }

    public EpisodeExpression[] EpisodeExpressions { get; set; }

    public EpisodeExpression[] MultipleEpisodeExpressions { get; set; }
}
