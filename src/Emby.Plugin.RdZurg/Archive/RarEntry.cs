using System;

namespace Emby.Plugin.RdZurg.Archive;

/// <summary>
/// One file stored inside a RAR archive, located as a byte range of the archive itself.
/// </summary>
/// <remarks>
/// Only <em>stored</em> members can be described this way. A compressed member's bytes are not
/// present in the archive in playable form, so it has no range to point a player at.
/// </remarks>
public sealed class RarEntry
{
    /// <summary>Gets the member's name as recorded in the archive.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the offset within the archive at which this member's bytes begin.</summary>
    public required long DataOffset { get; init; }

    /// <summary>Gets the member's length in bytes.</summary>
    public required long Length { get; init; }

    /// <summary>Gets a value indicating whether the member is stored rather than compressed.</summary>
    public required bool IsStored { get; init; }

    /// <summary>Translates a read of the member into a read of the archive.</summary>
    /// <param name="offset">Offset within the member.</param>
    /// <returns>The corresponding offset within the archive.</returns>
    public long ToArchiveOffset(long offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return DataOffset + offset;
    }
}
