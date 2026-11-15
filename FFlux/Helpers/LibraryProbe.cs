using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Screenbox.Core.Services;
using Windows.Storage;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only (<c>FFLUX_LIBRARY_PROBE=1</c>): runs the media library scan and writes what it
/// found to the startup log.
/// </summary>
/// <remarks>
/// The music library reads folders reported by Windows, and what those are depends on package
/// identity - something that cannot be checked from a unit test and cannot be seen on screen in
/// this host. The probe therefore reports the folders that will actually be scanned and the item
/// counts, which is enough to tell "scanned nothing" apart from "scanned and found nothing".
///
/// Set <c>FFLUX_LIBRARY_EXTRA</c> to a folder path to also exercise the add-folder flow: the probe
/// records the count before, adds the folder, rescans, and removes it again unless
/// <c>FFLUX_LIBRARY_EXTRA_KEEP=1</c> is set.
/// </remarks>
internal static class LibraryProbe
{
    public static async Task RunAsync()
    {
        // Let the normal startup fetch finish first so its result is the one being inspected.
        await Task.Delay(6000);

        try
        {
            StartupErrorLog.Write($"Library probe: package identity = {SystemInformation.HasPackageIdentity}");

            var context = Ioc.Default.GetRequiredService<LibraryContext>();
            var coordinator = Ioc.Default.GetRequiredService<ILibraryCoordinator>();
            var libraryService = Ioc.Default.GetRequiredService<ILibraryService>();

            await coordinator.EnsureWatchingAsync();

            context.MusicStorageLibrary ??= await libraryService.InitializeMusicLibraryAsync();
            context.VideosStorageLibrary ??= await libraryService.InitializeVideosLibraryAsync();

            string musicLibraryFolders = string.Join(" | ", context.MusicStorageLibrary?.Folders.Select(f => f.Path) ?? []);
            string videoLibraryFolders = string.Join(" | ", context.VideosStorageLibrary?.Folders.Select(f => f.Path) ?? []);
            StartupErrorLog.Write($"Library probe: music library folders = {musicLibraryFolders}");
            StartupErrorLog.Write($"Library probe: videos library folders = {videoLibraryFolders}");

            // Force a full scan: the point is to prove the folders are readable, not to measure the cache.
            await coordinator.FetchMusicAsync(false);
            await coordinator.FetchVideosAsync(false);

            if (context.MusicStorageLibrary is not null)
            {
                IReadOnlyList<StorageFolder> musicRoots =
                    await libraryService.GetLibraryRootsAsync(context.MusicStorageLibrary, MediaPlaybackType.Music);
                StartupErrorLog.Write($"Library probe: music scanned folders = {string.Join(" | ", musicRoots.Select(f => f.Path))}");
            }

            if (context.VideosStorageLibrary is not null)
            {
                IReadOnlyList<StorageFolder> videoRoots =
                    await libraryService.GetLibraryRootsAsync(context.VideosStorageLibrary, MediaPlaybackType.Video);
                StartupErrorLog.Write($"Library probe: videos scanned folders = {string.Join(" | ", videoRoots.Select(f => f.Path))}");
            }

            StartupErrorLog.Write($"Library probe: songs = {context.Music.Songs.Count}, " +
                                  $"albums = {context.Music.Albums.Count}, artists = {context.Music.Artists.Count}");
            StartupErrorLog.Write($"Library probe: videos = {context.Videos.Videos.Count}");

            foreach (var song in context.Music.Songs.Take(3))
            {
                StartupErrorLog.Write($"Library probe: song '{song.Name}' / {song.Location}");
            }

            foreach (var video in context.Videos.Videos.Take(3))
            {
                StartupErrorLog.Write($"Library probe: video '{video.Name}' / {video.Location}");
            }

            await ProbeAddFolderAsync(coordinator, context);
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Library probe failed: " + ex);
        }
    }

    private static async Task ProbeAddFolderAsync(ILibraryCoordinator coordinator, LibraryContext context)
    {
        string? path = Environment.GetEnvironmentVariable("FFLUX_LIBRARY_EXTRA");
        if (string.IsNullOrWhiteSpace(path)) return;

        // A POSIX shell rewrites the separators of an environment value; WinRT only accepts '\'.
        path = path.Replace('/', '\\');

        StorageFolder folder;
        try
        {
            folder = await StorageFolder.GetFolderFromPathAsync(path);
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write($"Library probe: extra folder '{path}' cannot be opened: {ex.Message}");
            return;
        }

        int before = context.Music.Songs.Count;
        bool added = await coordinator.AddMusicFolderAsync(folder);
        StartupErrorLog.Write($"Library probe: add folder '{folder.Path}' = {added}, " +
                              $"songs {before} -> {context.Music.Songs.Count}");

        bool keep = Environment.GetEnvironmentVariable("FFLUX_LIBRARY_EXTRA_KEEP") == "1";
        if (keep) return;

        int afterAdd = context.Music.Songs.Count;
        bool removed = await coordinator.RemoveMusicFolderAsync(folder);
        StartupErrorLog.Write($"Library probe: remove folder '{folder.Path}' = {removed}, " +
                              $"songs {afterAdd} -> {context.Music.Songs.Count}");

        // Give a late progress snapshot the chance to land, so a stale overwrite would show up.
        await Task.Delay(1500);
        StartupErrorLog.Write($"Library probe: songs after settle = {context.Music.Songs.Count}");
    }
}
