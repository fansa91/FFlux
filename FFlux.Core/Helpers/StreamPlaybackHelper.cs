using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using Screenbox.Core.Factories;
using Screenbox.Core.Messages;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Shared entry point for starting playback of a network URL from any surface.
/// </summary>
/// <remarks>
/// Everything that opens a URL (the dialog, the queue page, the stream library) funnels through
/// here so normalization and the FFmpeg option hand-off behave identically everywhere.
/// </remarks>
public static class StreamPlaybackHelper
{
    /// <summary>
    /// Normalizes <paramref name="url"/>, builds a <see cref="MediaViewModel"/> carrying
    /// <paramref name="options"/> and sends it to the player.
    /// </summary>
    /// <returns><see langword="true"/> when playback was requested.</returns>
    public static bool Play(
        IMessenger messenger,
        MediaViewModelFactory factory,
        string? url,
        string? name = null,
        IEnumerable<string>? options = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!StreamUrlDetector.TryNormalize(url, out Uri? uri, out _) || uri is null) return false;

        MediaViewModel media = factory.Create(uri, options, name);
        messenger.Send(new PlayMediaMessage(media));
        return true;
    }

    /// <summary>
    /// Plays the URL of an "open online link" dialog result.
    /// </summary>
    public static bool Play(
        IMessenger messenger,
        MediaViewModelFactory factory,
        OpenUrlRequest? request)
    {
        return request is not null &&
               Play(messenger, factory, request.Url, null, request.ToFFmpegOptions());
    }
}
