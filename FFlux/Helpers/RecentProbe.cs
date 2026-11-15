using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Screenbox.Core.Enums;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Windows.Storage;
using Windows.Storage.Search;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only helper (<c>FFLUX_RECENT_PROBE=read|write|clear</c>): reports what the playback
/// history holds and, in <c>write</c> mode, records one real music file so a following <c>read</c>
/// run can prove the entry survives a restart.
/// </summary>
internal static class RecentProbe
{
    public static async Task RunAsync(string mode, Action<string> log)
    {
        try
        {
            log($"mode={mode}");
            IDatabaseService db = Ioc.Default.GetRequiredService<IDatabaseService>();
            IFilesService files = Ioc.Default.GetRequiredService<IFilesService>();

            IReadOnlyList<string> before = await db.GetRecentItemPathsAsync(50);
            log($"rows before = {before.Count}");
            foreach (string path in before.Take(3))
            {
                log("  row: " + path);
            }

            if (string.Equals(mode, "write", StringComparison.OrdinalIgnoreCase))
            {
                StorageFile? sample = await FindSampleAudioAsync(log);
                if (sample is null)
                {
                    log("no sample audio file found");
                    return;
                }

                log("tracking " + sample.Path);
                files.AddToRecent(sample);
                // AddToRecent is fire-and-forget on purpose, so give the write a moment.
                await Task.Delay(1500);

                IReadOnlyList<string> after = await db.GetRecentItemPathsAsync(50);
                log($"rows after = {after.Count}, top = {after.FirstOrDefault()}");

                await VerifyPruningAsync(db, files, log);
            }
            else if (string.Equals(mode, "clear", StringComparison.OrdinalIgnoreCase))
            {
                await files.ClearRecentAsync();
                log($"rows after clear = {(await db.GetRecentItemPathsAsync(50)).Count}");
            }

            // Exercise the real home page path: the view model reads the list from the database.
            HomePageViewModel home = Ioc.Default.GetRequiredService<HomePageViewModel>();
            home.OnLoaded();
            await Task.Delay(2500);
            log($"home recent count = {home.Recent.Count}");
            foreach (MediaViewModel media in home.Recent.Take(3))
            {
                log("  home: " + media.Location);
            }
        }
        catch (Exception e)
        {
            log("FAILED " + e);
        }
    }

    /// <summary>
    /// Verifies that a file the user deleted is dropped from the history instead of lingering as a
    /// dead tile. Uses a throwaway copy under %TEMP% so no real media file is touched.
    /// </summary>
    private static async Task VerifyPruningAsync(IDatabaseService db, IFilesService files, Action<string> log)
    {
        string folder = Path.Combine(Path.GetTempPath(), "fflux-recent-probe");
        string path = Path.Combine(folder, "gone.mp3");
        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(path, [0x49, 0x44, 0x33]);

            files.AddToRecent(await StorageFile.GetFileFromPathAsync(path));
            await Task.Delay(1200);
            bool tracked = (await db.GetRecentItemPathsAsync(50)).Any(p => p == path);
            log($"prune: temp file tracked = {tracked}");

            File.Delete(path);
            IReadOnlyList<StorageFile> resolved = await files.GetRecentFilesAsync(50);
            bool stillListed = (await db.GetRecentItemPathsAsync(50)).Any(p => p == path);
            log($"prune: after deleting it still in history = {stillListed}, resolved = {resolved.Count}");
        }
        catch (Exception e)
        {
            log("prune FAILED " + e);
        }
        finally
        {
            // A kept row would be invisible forever once the folder is gone, so drop it by hand.
            try
            {
                await db.RemoveRecentItemAsync(path);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // best effort cleanup
            }
        }
    }

    private static async Task<StorageFile?> FindSampleAudioAsync(Action<string> log)
    {
        // Goes through the same helpers the app uses to scan the library, so the sample is a file
        // the app really considers part of the music library.
        ILibraryService library = Ioc.Default.GetRequiredService<ILibraryService>();
        StorageLibrary music = await library.InitializeMusicLibraryAsync();
        IReadOnlyList<StorageFolder> roots = await library.GetLibraryRootsAsync(music, MediaPlaybackType.Music);
        log($"music roots = {roots.Count}");
        foreach (StorageFolder root in roots)
        {
            log("  root: " + root.Path);
        }

        foreach (StorageFileQueryResult query in library.CreateMusicLibraryQueries(roots, useIndexer: false))
        {
            try
            {
                IReadOnlyList<StorageFile> files = await query.GetFilesAsync(0, 3);
                if (files.Count > 0) return files[0];
            }
            catch (Exception)
            {
                // Unreadable root; try the next one.
            }
        }

        return null;
    }
}
