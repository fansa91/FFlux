using System.Collections.Generic;
using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;

namespace Screenbox.Core.Services;

/// <summary>
/// Manages the application's SQLite database.
/// The database acts as a quick cache layer; data loss is handled gracefully by recreating the database.
/// </summary>
public interface IDatabaseService
{
    /// <summary>
    /// Initializes the database, creating it if necessary and applying the schema.
    /// If corruption is detected, the database file is deleted and recreated from scratch.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Reads cached library folders and media records for the requested media type.
    /// </summary>
    Task<RawCacheLoadResultDto> LoadLibraryCacheAsync(MediaPlaybackType mediaType);

    /// <summary>
    /// Loads the cheap size / last-modified fingerprints that let a scan reuse cached metadata
    /// when the Windows change tracker is unavailable (unpackaged builds).
    /// </summary>
    Task<IReadOnlyDictionary<string, FileStampDto>> LoadFileStampsAsync(MediaPlaybackType mediaType);

    /// <summary>Differentially updates the file fingerprints for one media type.</summary>
    Task SaveFileStampsAsync(MediaPlaybackType mediaType, IReadOnlyDictionary<string, FileStampDto> stamps);

    /// <summary>Saves the complete cached music snapshot to the database.</summary>
    Task SaveMusicCacheAsync(IReadOnlyList<string> folderPaths, IReadOnlyList<MusicCacheRecordDto> records);

    /// <summary>Saves the complete cached video snapshot to the database.</summary>
    Task SaveVideoCacheAsync(IReadOnlyList<string> folderPaths, IReadOnlyList<VideoCacheRecordDto> records);

    /// <summary>Replaces playback progress rows with the provided snapshot.</summary>
    Task ReplacePlaybackProgressAsync(IReadOnlyList<MediaPlaybackProgress> snapshot);

    /// <summary>Loads all playback progress entries.</summary>
    Task<List<MediaPlaybackProgress>> LoadPlaybackProgressAsync();

    /// <summary>Persists a playlist and its items.</summary>
    Task SavePlaylistAsync(PlaylistRecordDto playlist);

    /// <summary>Loads a playlist and its items, or null when not found.</summary>
    Task<PlaylistRecordDto?> LoadPlaylistAsync(string id);

    /// <summary>Lists all playlists with their items.</summary>
    Task<List<PlaylistRecordDto>> ListPlaylistsAsync();

    /// <summary>Deletes a playlist and cascades to its items.</summary>
    Task DeletePlaylistAsync(string id);

    /// <summary>
    /// Lists the folders the user added to a library by hand, in the order they were added.
    /// </summary>
    Task<IReadOnlyList<string>> GetLibraryRootsAsync(MediaPlaybackType mediaType);

    /// <summary>
    /// Records a folder the user added to a library. Answers false when it was already there.
    /// </summary>
    Task<bool> AddLibraryRootAsync(MediaPlaybackType mediaType, string path);

    /// <summary>
    /// Forgets a folder the user added to a library. Answers false when it was not there.
    /// </summary>
    Task<bool> RemoveLibraryRootAsync(MediaPlaybackType mediaType, string path);

    /// <summary>
    /// Lists the paths of the most recently played files, newest first.
    /// </summary>
    Task<IReadOnlyList<string>> GetRecentItemPathsAsync(int limit);

    /// <summary>
    /// Records that a file was played at the given time, replacing any earlier row for that path.
    /// </summary>
    Task TouchRecentItemAsync(string path, long playedAtUnixMilliseconds);

    /// <summary>Forgets a single played file.</summary>
    Task RemoveRecentItemAsync(string path);

    /// <summary>Forgets the whole playback history.</summary>
    Task ClearRecentItemsAsync();
}
