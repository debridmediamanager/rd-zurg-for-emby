// Derived from MediaBrowser/Emby.Naming (commit c2a5776, 2019-05-16), MIT licensed; see LICENSE-Emby.Naming.md.
// Kept close to upstream on purpose. The plugin's own changes to the expressions live in Library/ReleaseNames.cs.
using System;
using System.Text.RegularExpressions;

namespace Emby.Plugin.RdZurg.Naming;

internal sealed class EpisodeExpression
{
    private string _expression = string.Empty;
    private Regex? _regex;

    public EpisodeExpression(string expression, bool byDate = false)
    {
        Expression = expression;
        IsByDate = byDate;
        DateTimeFormats = Array.Empty<string>();
        SupportsAbsoluteEpisodeNumbers = true;
    }

    public string Expression
    {
        get => _expression;
        set
        {
            _expression = value;
            _regex = null;
        }
    }

    public bool IsByDate { get; set; }

    public bool IsOptimistic { get; set; }

    public bool IsNamed { get; set; }

    public bool SupportsAbsoluteEpisodeNumbers { get; set; }

    public string[] DateTimeFormats { get; set; }

    public Regex Regex => _regex ??= new Regex(Expression, RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
