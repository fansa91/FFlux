using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Screenbox.Core.Contexts;
using Screenbox.Core.Coordinators;
using Screenbox.Core.Helpers;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only (<c>FFLUX_LIBRARY_PROBE=1</c>): runs the media library scan and writes what it
/// found to the startup log.
/// </summary>
/// <remarks>
/// The music library reads a known folder, and whether that works depends on package identity -
/// something that cannot be checked from a unit test and cannot be seen on screen in this host.
/// The probe therefore reports the resolved root folders and the item counts, which is enough to
/// tell "scanned nothing" apart from "scanned and found nothing".
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

            await coordinator.EnsureWatchingAsync();

            context.MusicStorageLibrary ??= await Ioc.Default
                .GetRequiredService<Screenbox.Core.Services.ILibraryService>()
                .InitializeMusicLibraryAsync();
            context.VideosStorageLibrary ??= await Ioc.Default
                .GetRequiredService<Screenbox.Core.Services.ILibraryService>()
                .InitializeVideosLibraryAsync();

            string musicRoots = string.Join(" | ", context.MusicStorageLibrary?.Folders.Select(f => f.Path) ?? []);
            string videoRoots = string.Join(" | ", context.VideosStorageLibrary?.Folders.Select(f => f.Path) ?? []);
            StartupErrorLog.Write($"Library probe: music roots = {musicRoots}");
            StartupErrorLog.Write($"Library probe: videos roots = {videoRoots}");

            // Force a full scan: the point is to prove the folder is readable, not to measure the cache.
            await coordinator.FetchMusicAsync(false);
            await coordinator.FetchVideosAsync(false);

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
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Library probe failed: " + ex);
        }
    }
}
