using System;
using System.Collections.Generic;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Merges the folders of a Windows library with the folders the user added by hand.
/// </summary>
/// <remarks>
/// Deliberately free of WinRT types so the merge rules can be unit tested. Two rules matter:
/// an exact duplicate is dropped, because scanning the same folder twice would list every song
/// twice; and a folder nested inside an already accepted folder is dropped, because querying the
/// parent already returns its files.
/// </remarks>
public static class LibraryRootPaths
{
    private static readonly char[] TrailingSeparators = ['\\', '/'];

    /// <summary>
    /// Merges the two folder lists, keeping the library folders first.
    /// </summary>
    /// <param name="libraryPaths">Folders that belong to the Windows library.</param>
    /// <param name="extraPaths">Folders the user added by hand.</param>
    /// <returns>Distinct, non-nested paths in scan order.</returns>
    public static IReadOnlyList<string> Merge(IEnumerable<string?> libraryPaths, IEnumerable<string?> extraPaths)
    {
        var result = new List<string>();
        Append(result, libraryPaths);
        Append(result, extraPaths);
        return result;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is already covered by one of <paramref name="roots"/>,
    /// either because it is the same folder or because it lives inside one.
    /// </summary>
    public static bool Covers(IEnumerable<string?> roots, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return false;
        string normalized = Normalize(candidate);
        if (normalized.Length == 0) return false;

        foreach (string? root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            if (IsSameOrInside(normalized, Normalize(root))) return true;
        }

        return false;
    }

    private static void Append(List<string> result, IEnumerable<string?> paths)
    {
        if (paths is null) return;

        foreach (string? path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            string normalized = Normalize(path);
            if (normalized.Length == 0) continue;

            bool alreadyCovered = false;
            foreach (string accepted in result)
            {
                if (!IsSameOrInside(normalized, accepted)) continue;
                alreadyCovered = true;
                break;
            }

            if (!alreadyCovered) result.Add(normalized);
        }
    }

    private static bool IsSameOrInside(string candidate, string parent)
    {
        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase)) return true;

        // "C:\" already ends with a separator; appending another one would never match.
        bool parentHasSeparator = parent.Length > 0 && Array.IndexOf(TrailingSeparators, parent[^1]) >= 0;
        string prefix = parentHasSeparator ? parent : parent + "\\";
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
    {
        string trimmed = path.Trim();
        // A drive root ("C:\") must keep its separator or it becomes an unusable "C:".
        return trimmed.Length <= 3 ? trimmed : trimmed.TrimEnd(TrailingSeparators);
    }
}
