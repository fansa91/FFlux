namespace Screenbox.Core.Models;

/// <summary>
/// A cheap on-disk fingerprint used to decide whether cached media metadata is still valid.
/// Size + last-modified is enough to catch edits, re-downloads and tag rewrites without opening
/// the container with FFmpeg.
/// </summary>
public sealed record class FileStampDto
{
    public string Path { get; set; } = string.Empty;

    public long Size { get; set; }

    /// <summary><c>DateTimeOffset.UtcTicks</c> of the file's last modification.</summary>
    public long ModifiedTicks { get; set; }
}
