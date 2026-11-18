using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Finds an LRC lyric file next to a media file, following the same unpackaged-safe
/// approach as <see cref="AlbumArtworkLoader"/>.
/// </summary>
public static class LyricsLoader
{
    private const string LyricExtension = ".lrc";

    // Media folder path -> file names present (null means "already searched, nothing found").
    private static readonly ConcurrentDictionary<string, string[]?> FolderCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Ranks file names as lyric candidates for a track: exact stem match first, then a shared
    /// "lyrics.lrc", then any LRC in the folder. Pure function for unit testing.
    /// </summary>
    public static IReadOnlyList<string> OrderCandidates(IEnumerable<string> fileNames, string mediaFileName)
    {
        if (fileNames is null) throw new ArgumentNullException(nameof(fileNames));
        if (string.IsNullOrWhiteSpace(mediaFileName)) return Array.Empty<string>();

        string stem = System.IO.Path.GetFileNameWithoutExtension(mediaFileName);
        var lrcFiles = fileNames
            .Where(f => f.EndsWith(LyricExtension, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var ranked = new List<string>();
        void Add(string name)
        {
            string? hit = lrcFiles.FirstOrDefault(f => string.Equals(f, name, StringComparison.OrdinalIgnoreCase));
            if (hit is not null && !ranked.Contains(hit, StringComparer.OrdinalIgnoreCase)) ranked.Add(hit);
        }

        Add(stem + LyricExtension);
        Add("lyrics" + LyricExtension);
        Add("lyric" + LyricExtension);
        foreach (string file in lrcFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (!ranked.Contains(file, StringComparer.OrdinalIgnoreCase)) ranked.Add(file);
        }

        return ranked;
    }

    /// <summary>Returns the best matching lyric file, or null when none is available.</summary>
    public static async Task<StorageFile?> FindAsync(StorageFile mediaFile)
    {
        try
        {
            StorageFolder? folder = await mediaFile.GetParentAsync();
            if (folder is null) return null;

            string? folderPath;
            try { folderPath = folder.Path; }
            catch (Exception) { folderPath = null; }

            string[]? names;
            if (folderPath is not null && FolderCache.TryGetValue(folderPath, out names))
            {
                // Cached value (including a recorded "no files" result).
            }
            else
            {
                names = await ReadNamesAsync(folder);
                if (folderPath is not null) FolderCache[folderPath] = names;
            }

            if (names is null || names.Length == 0) return null;

            foreach (string candidate in OrderCandidates(names, mediaFile.Name))
            {
                try
                {
                    return await folder.GetFileAsync(candidate);
                }
                catch (Exception)
                {
                    // The file may have disappeared since the directory was listed.
                }
            }
        }
        catch (Exception)
        {
            // Storage APIs throw without package identity in some configurations; lyrics are optional.
        }

        return null;
    }

    public static void ClearCaches() => FolderCache.Clear();

    private static async Task<string[]?> ReadNamesAsync(StorageFolder folder)
    {
        try
        {
            IReadOnlyList<StorageFile> files = await folder.GetFilesAsync();
            return files.Select(f => f.Name).ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
