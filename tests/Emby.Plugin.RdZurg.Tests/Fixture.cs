using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Emby.Plugin.RdZurg.Tests;

internal static class Fixture
{
    public static string PathOf(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static string Text(string name) => File.ReadAllText(PathOf(name));

    /// <summary>Rewrites a gzipped fixture in the source tree, not the copy in the build output.</summary>
    public static void WriteTsv(string name, string header, IEnumerable<string> lines)
    {
        var source = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures", name));
        using var file = File.Create(source);
        using var gzip = new GZipStream(file, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip, new UTF8Encoding(false));
        writer.WriteLine(header);
        foreach (var line in lines)
        {
            writer.WriteLine(line);
        }
    }

    /// <summary>Reads a gzipped, tab-separated fixture, skipping comment lines.</summary>
    public static IEnumerable<string[]> Tsv(string name)
    {
        using var file = File.OpenRead(PathOf(name));
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            yield return line.Split('\t');
        }
    }
}
