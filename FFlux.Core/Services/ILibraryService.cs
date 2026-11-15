using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Windows.Storage;
using Windows.Storage.Search;

namespace Screenbox.Core.Services;

/// <summary>
/// Stateless service for library management operations.
/// Does not access or modify <see cref="Contexts.LibraryContext"/> directly;
/// all context mutations are the responsibility of the caller (typically <see cref="Coordinators.ILibraryCoordinator"/>).
/// </summary>
public interface ILibraryService
{
    /// <summary>
    /// Initialize the music library
    /// </summary>
    Task<StorageLibrary> InitializeMusicLibraryAsync();

    /// <summary>
    /// Initialize the videos library
    /// </summary>
    Task<StorageLibrary> InitializeVideosLibraryAsync();

    /// <summary>
    /// Lists every folder a library has to scan: the folders Windows reports for that library
    /// followed by the folders the user added by hand. Duplicates and nested folders are dropped.
    /// </summary>
    Task<IReadOnlyList<StorageFolder>> GetLibraryRootsAsync(StorageLibrary library, MediaPlaybackType mediaType);

    /// <summary>
    /// Remembers a folder the user added to a library. Answers false when the folder was already
    /// covered by the existing roots or cannot be persisted.
    /// </summary>
    Task<bool> AddLibraryRootAsync(StorageLibrary library, MediaPlaybackType mediaType, StorageFolder folder);

    /// <summary>
    /// Forgets a folder the user added to a library. Answers false when it was not a hand-added folder.
    /// </summary>
    Task<bool> RemoveLibraryRootAsync(MediaPlaybackType mediaType, StorageFolder folder);

    /// <summary>
    /// Fetch music from the library. Returns a <see cref="MusicLibrary"/> that the caller
    /// should apply to the library context. Intermediate results are reported via <paramref name="progress"/>.
    /// </summary>
    Task<MusicLibrary> FetchMusicAsync(
        LibraryScanScope scope,
        bool useCache,
        CancellationToken cancellationToken,
        IProgress<MusicLibrary>? progress = null);

    /// <summary>
    /// Fetch videos from the library. Returns a <see cref="VideosLibrary"/> that the caller
    /// should apply to the library context. Intermediate results are reported via <paramref name="progress"/>.
    /// </summary>
    Task<VideosLibrary> FetchVideosAsync(
        LibraryScanScope scope,
        bool useCache,
        CancellationToken cancellationToken,
        IProgress<VideosLibrary>? progress = null);

    /// <summary>
    /// Creates one query per library folder for music.
    /// </summary>
    /// <param name="roots">The folders the scan covers, as returned by <see cref="GetLibraryRootsAsync"/>.</param>
    /// <param name="useIndexer">Whether the system search indexer may be used.</param>
    IReadOnlyList<StorageFileQueryResult> CreateMusicLibraryQueries(IReadOnlyList<StorageFolder> roots, bool useIndexer);

    /// <summary>
    /// Creates one query per library folder for videos.
    /// </summary>
    /// <param name="roots">The folders the scan covers, as returned by <see cref="GetLibraryRootsAsync"/>.</param>
    /// <param name="useIndexer">Whether the system search indexer may be used.</param>
    IReadOnlyList<StorageFileQueryResult> CreateVideosLibraryQueries(IReadOnlyList<StorageFolder> roots, bool useIndexer);
}
