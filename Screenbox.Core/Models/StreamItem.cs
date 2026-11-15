using System;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Models;

/// <summary>
/// One saved entry in the network stream library.
/// </summary>
/// <remarks>
/// <para>
/// Persisted by <see cref="Services.StreamManagerService"/> as plain JSON, so every property has
/// a public setter and the type avoids constructor parameters that <c>System.Text.Json</c>
/// cannot bind. <see cref="BadgeText"/> is derived from <see cref="FormatType"/> and is therefore
/// kept out of the serialized payload: storing it would let a stale badge survive a format fix.
/// </para>
/// <para>
/// The anti-hotlink fields are passed to FFmpeg verbatim as <c>user_agent</c>, <c>referer</c> and
/// <c>headers</c> AVFormat options.
/// </para>
/// </remarks>
public sealed partial class StreamItem : ObservableObject
{
    /// <summary>Stable identity used to correlate the list, the JSON file and the UI.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    [ObservableProperty] public partial string Name { get; set; } = string.Empty;

    [ObservableProperty] public partial string Url { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    public partial StreamFormatType FormatType { get; set; }

    [ObservableProperty] public partial string UserAgent { get; set; } = string.Empty;

    [ObservableProperty] public partial string Referer { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets extra HTTP headers, one <c>Name: Value</c> pair per line.
    /// </summary>
    [ObservableProperty]
    public partial string Headers { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// Gets the short format label shown in the list, e.g. <c>HLS</c> or <c>RTSP</c>.
    /// </summary>
    [JsonIgnore]
    public string BadgeText => FormatType.ToBadgeText();

    [JsonIgnore]
    public bool HasAntiHotlinkOptions =>
        !string.IsNullOrWhiteSpace(UserAgent) ||
        !string.IsNullOrWhiteSpace(Referer) ||
        !string.IsNullOrWhiteSpace(Headers);

    /// <summary>
    /// Returns a copy that is safe to hand to an editor dialog: the dialog mutates the copy and
    /// the caller applies it back only when the user confirms.
    /// </summary>
    public StreamItem Clone() => new()
    {
        Id = Id,
        Name = Name,
        Url = Url,
        FormatType = FormatType,
        UserAgent = UserAgent,
        Referer = Referer,
        Headers = Headers,
        CreatedAt = CreatedAt,
    };

    /// <summary>
    /// Copies every editable field from <paramref name="other"/> without touching the identity.
    /// </summary>
    public void CopyFrom(StreamItem other)
    {
        ArgumentNullException.ThrowIfNull(other);
        Name = other.Name;
        Url = other.Url;
        FormatType = other.FormatType;
        UserAgent = other.UserAgent;
        Referer = other.Referer;
        Headers = other.Headers;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Url : Name;
}
