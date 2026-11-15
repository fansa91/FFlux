using System;
using System.Collections.Generic;
using System.Linq;
using Screenbox.Core.Models;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Pure rules for deciding whether a library cache snapshot is still usable. Kept free of WinRT
/// types so the incremental-scan policy can be unit tested in the plain desktop test host.
/// </summary>
public static class LibraryCacheDiff
{
    /// <summary>
    /// Answers whether the file set changed, ignoring order: a fingerprint scan walks queries in
    /// title order while the database returns rows in primary-key order.
    /// </summary>
    public static bool HaveSamePaths(IEnumerable<string> currentPaths, IEnumerable<string> cachedPaths)
    {
        ArgumentNullException.ThrowIfNull(currentPaths);
        ArgumentNullException.ThrowIfNull(cachedPaths);

        HashSet<string> current = new(currentPaths, StringComparer.OrdinalIgnoreCase);
        HashSet<string> cached = new(cachedPaths, StringComparer.OrdinalIgnoreCase);
        return current.SetEquals(cached);
    }

    /// <summary>
    /// Answers whether any file's size / last-modified fingerprint changed. A null or empty
    /// <paramref name="known"/> against a non-empty current set means "no baseline yet", so every
    /// file has to be parsed once to build it.
    /// </summary>
    public static bool HaveStampsChanged(
        IReadOnlyDictionary<string, FileStampDto>? known,
        IReadOnlyDictionary<string, FileStampDto> current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (known is null || known.Count != current.Count) return true;

        foreach (KeyValuePair<string, FileStampDto> pair in current)
        {
            if (!known.TryGetValue(pair.Key, out FileStampDto? old)
                || old.Size != pair.Value.Size
                || old.ModifiedTicks != pair.Value.ModifiedTicks)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Paths whose metadata has to be parsed again: everything new or whose fingerprint moved.
    /// </summary>
    public static IReadOnlyList<string> SelectChangedPaths(
        IReadOnlyDictionary<string, FileStampDto>? known,
        IReadOnlyDictionary<string, FileStampDto> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        return current
            .Where(pair => known is null
                           || !known.TryGetValue(pair.Key, out FileStampDto? old)
                           || old.Size != pair.Value.Size
                           || old.ModifiedTicks != pair.Value.ModifiedTicks)
            .Select(pair => pair.Key)
            .ToList();
    }
}
