using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Screenbox.Core.Enums;
using Windows.Storage;

namespace Screenbox.Core.Helpers;

public static class FilesHelpers
{
    public static ImmutableArray<string> SupportedAudioFormats { get; } =
        ImmutableArray.Create(".mp3", ".wav", ".wma", ".aac", ".mid", ".midi", ".mpa", ".ogg", ".oga", ".opus", ".weba", ".flac", ".m4a", ".m4b", ".wv", ".wvc", ".aiff", ".aif", ".aifc", ".ac3", ".ape", ".dts", ".nist", ".ra", ".spx");

    public static ImmutableArray<string> SupportedVideoFormats { get; } =
        ImmutableArray.Create(".avi", ".mp4", ".wmv", ".mov", ".mkv", ".flv", ".3gp", ".3g2", ".m4v", ".mpg", ".mpeg", ".webm", ".rm", ".rmvb", ".asf", ".wm", ".wtv", ".f4v", ".swf", ".vob", ".mxf", ".ogv", ".ogm", ".ts", ".mts", ".m2ts", ".m2t");

    public static ImmutableArray<string> SupportedPlaylistFormats { get; } =
        ImmutableArray.Create(".m3u8", ".m3u");

    public static ImmutableArray<string> SupportedFormats { get; } =
        SupportedVideoFormats.AddRange(SupportedAudioFormats).AddRange(SupportedPlaylistFormats);

    public static ImmutableArray<string> SupportedSubtitleFormats { get; } = ImmutableArray.Create(".srt", ".vtt", ".ass", ".idx", ".sub");

    public static bool IsSupportedAudio(this IStorageFile file) => IsSupportedAudio(file.FileType);
    public static bool IsSupportedVideo(this IStorageFile file) => IsSupportedVideo(file.FileType);
    public static bool IsSupportedPlaylist(this IStorageFile file) => IsSupportedPlaylist(file.FileType);
    public static bool IsSupported(this IStorageFile file) => IsSupported(file.FileType);
    public static bool IsSupportedSubtitle(this IStorageFile file) => IsSupportedSubtitle(file.FileType);

    public static bool IsSupportedAudio(string extension) => SupportedAudioFormats.Contains(NormalizeExtension(extension));
    public static bool IsSupportedVideo(string extension) => SupportedVideoFormats.Contains(NormalizeExtension(extension));
    public static bool IsSupportedPlaylist(string extension) => SupportedPlaylistFormats.Contains(NormalizeExtension(extension));
    public static bool IsSupported(string extension) => SupportedFormats.Contains(NormalizeExtension(extension));
    public static bool IsSupportedSubtitle(string extension) => SupportedSubtitleFormats.Contains(NormalizeExtension(extension));

    public static MediaPlaybackType GetMediaTypeForExtension(string extension)
    {
        if (IsSupportedVideo(extension)) return MediaPlaybackType.Video;
        if (IsSupportedAudio(extension)) return MediaPlaybackType.Music;
        if (IsSupportedPlaylist(extension)) return MediaPlaybackType.Playlist;
        return MediaPlaybackType.Unknown;
    }

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension)) return string.Empty;
        return extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    public static MediaPlaybackType GetMediaTypeForFile(IStorageFile file)
    {
        if (file.IsSupportedVideo()) return MediaPlaybackType.Video;
        if (file.IsSupportedAudio()) return MediaPlaybackType.Music;
        if (file.ContentType.StartsWith("image")) return MediaPlaybackType.Image;
        if (file.IsSupportedPlaylist()) return MediaPlaybackType.Playlist;
        return MediaPlaybackType.Unknown;
    }

    /// <summary>
    /// Rewrites a file-system path into the form the <c>Windows.Storage</c> path APIs accept.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StorageFile.GetFileFromPathAsync</c> and <c>StorageFolder.GetFolderFromPathAsync</c>
    /// reject any path that contains a forward slash, returning
    /// <c>ERROR_BAD_PATHNAME</c> (0x800700A1) even when the file is there and readable. The
    /// rejection is not extension dependent: the same file opens fine with backslashes and
    /// fails with slashes.
    /// </para>
    /// <para>
    /// Forward slashes reach the app from command lines, from paths copied out of a shell or a
    /// URL, and from anything written on a non-Windows platform, so they are normalised here
    /// rather than at each call site. The <c>\\?\</c> extended-length prefix is stripped for the
    /// same reason: those APIs reject it too.
    /// </para>
    /// </remarks>
    /// <param name="path">The path to normalise. May already be well formed.</param>
    /// <returns>A rooted path using backslashes only.</returns>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string normalized = path.Trim();

        const string extendedPrefix = @"\\?\";
        const string extendedUncPrefix = @"\\?\UNC\";
        if (normalized.StartsWith(extendedUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            normalized = @"\\" + normalized[extendedUncPrefix.Length..];
        }
        else if (normalized.StartsWith(extendedPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[extendedPrefix.Length..];
        }

        if (normalized.IndexOf('/') >= 0)
        {
            normalized = normalized.Replace('/', '\\');
        }

        try
        {
            normalized = System.IO.Path.GetFullPath(normalized);
        }
        catch (Exception)
        {
            // Leave relative or malformed input untouched: the caller reports the failure from
            // the actual API call, which has a much better error message than anything here.
        }

        return normalized;
    }

    /// <summary>
    /// Attempts to retrieve a <see cref="StorageFile"/> from a file-system path.
    /// Returns <see langword="null"/> when the file is inaccessible or does not exist.
    /// </summary>
    public static async Task<StorageFile?> TryGetFileFromPathAsync(string path)
    {
        try
        {
            return await StorageFile.GetFileFromPathAsync(NormalizePath(path));
        }
        catch (Exception)
        {
            return null;
        }
    }
}
