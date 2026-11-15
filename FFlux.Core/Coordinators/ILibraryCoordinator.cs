using System;
using System.Threading.Tasks;
using Windows.Storage;

namespace Screenbox.Core.Coordinators;

/// <summary>
/// Represents a stateful coordinator that owns library watchers and timers,
/// and drives library fetch operations through <see cref="Services.ILibraryService"/>.
/// </summary>
public interface ILibraryCoordinator : IDisposable
{
    /// <summary>
    /// Ensures query watchers are created and attached to the current context.
    /// Safe to call multiple times; no-ops if already watching.
    /// </summary>
    Task EnsureWatchingAsync();

    /// <summary>
    /// Recreates query watchers. Call when settings that affect queries (e.g. indexer usage) change.
    /// </summary>
    Task RefreshWatchersAsync();

    /// <summary>
    /// Fetches the music library and applies the result to <see cref="Contexts.LibraryContext"/>.
    /// </summary>
    Task FetchMusicAsync(bool useCache = true);

    /// <summary>
    /// Fetches the videos library and applies the result to <see cref="Contexts.LibraryContext"/>.
    /// </summary>
    Task FetchVideosAsync(bool useCache = true);

    /// <summary>
    /// Adds a folder to the music library and rescans. Answers false when the folder was already
    /// covered by the folders the library scans.
    /// </summary>
    Task<bool> AddMusicFolderAsync(StorageFolder folder);

    /// <summary>
    /// Removes a hand-added folder from the music library and rescans. Answers false when the
    /// folder was not added by hand, in which case it belongs to the Windows library definition.
    /// </summary>
    Task<bool> RemoveMusicFolderAsync(StorageFolder folder);

    /// <summary>
    /// Adds a folder to the videos library and rescans. See <see cref="AddMusicFolderAsync"/>.
    /// </summary>
    Task<bool> AddVideosFolderAsync(StorageFolder folder);

    /// <summary>
    /// Removes a hand-added folder from the videos library and rescans.
    /// See <see cref="RemoveMusicFolderAsync"/>.
    /// </summary>
    Task<bool> RemoveVideosFolderAsync(StorageFolder folder);
}
