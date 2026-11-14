using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FFmpegInteropX;
using Screenbox.Core.Helpers;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace Screenbox.Core.Playback;

/// <summary>
/// Opt-in scripted playback exercise. Screenbox has no automation surface, so verifying
/// track switching, external subtitles, FastSeek and hardware decoding means driving the
/// player from code and reading the result out of the trace file.
/// Enable with <c>SCREENBOX_PLAYBACK_PROBE=1</c>; point <c>SCREENBOX_PROBE_SUBTITLE</c> at a
/// subtitle file to exercise the external-subtitle path.
/// </summary>
public static class PlaybackProbe
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("SCREENBOX_PLAYBACK_PROBE") == "1";

    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(900);
    private static int _started;

    /// <summary>
    /// Starts the probe once, after the first media opened.
    /// </summary>
    internal static void TryStart(FFmpegMediaPlayer player, FFmpegPlaybackItem item)
    {
        if (!Enabled || Interlocked.Exchange(ref _started, 1) != 0)
        {
            return;
        }

        _ = RunAsync(player, item);
    }

    private static async Task RunAsync(FFmpegMediaPlayer player, FFmpegPlaybackItem item)
    {
        try
        {
            // Normally raised even earlier, in FFmpegMediaSourceConfigFactory, before the
            // source is opened. Kept here as a fallback for callers that build configs directly.
            try
            {
                FFmpegInteropLogging.SetLogLevel(LogLevel.Debug);
            }
            catch (Exception ex)
            {
                Log("SetLogLevel failed: " + ex.Message);
            }

            // Give the first frames a chance to render so decoder info is populated.
            await Task.Delay(1200);

            DescribeMedia(player, item);
            await SwitchAudioAsync(player, item);
            await ExerciseAudioDelayAsync(player);
            await SelectEmbeddedSubtitleAsync(item);
            await AddExternalSubtitleAsync(player, item);
            await SeekAsync(player, TimeSpan.FromSeconds(5.4), "forward");
            await SeekAsync(player, TimeSpan.FromSeconds(1.2), "backward");
            await ChangeRateAsync(player, 2.0);
            await ChangeRateAsync(player, 1.0);

            Log("done");
        }
        catch (Exception ex)
        {
            PlaybackTrace.Write("probe FAILED", ex);
        }
    }

    private static void DescribeMedia(FFmpegMediaPlayer player, FFmpegPlaybackItem item)
    {
        Log($"media: duration={player.NaturalDuration} size={player.NaturalVideoWidth}x{player.NaturalVideoHeight} " +
            $"state={player.PlaybackState} rate={player.PlaybackRate}");

        FFmpegMediaSource? source = item.FFmpegSource;
        if (source is null)
        {
            Log("media: no FFmpeg source");
            return;
        }

        foreach (VideoStreamInfo video in source.VideoStreams)
        {
            Log($"  video[{video.StreamIndex}] codec={video.CodecName} {video.PixelWidth}x{video.PixelHeight} " +
                $"fps={video.FramesPerSecond:0.##} engine={video.DecoderEngine} hw={video.HardwareDecoderStatus} " +
                $"lang={video.Language} name='{video.Name}'");
        }

        foreach (AudioStreamInfo audio in source.AudioStreams)
        {
            Log($"  audio[{audio.StreamIndex}] codec={audio.CodecName} ch={audio.Channels} " +
                $"rate={audio.SampleRate} lang={audio.Language} name='{audio.Name}'");
        }

        foreach (SubtitleStreamInfo subtitle in source.SubtitleStreams)
        {
            Log($"  subtitle[{subtitle.StreamIndex}] codec={subtitle.CodecName} lang={subtitle.Language} " +
                $"external={subtitle.IsExternal} name='{subtitle.Name}'");
        }

        FFmpegAudioTrackList audioTracks = item.AudioTracks;
        Log($"audio tracks: count={audioTracks.Count} selected={audioTracks.SelectedIndex} " +
            $"labels=[{DescribeTracks(audioTracks)}]");

        FFmpegSubtitleTrackList subtitleTracks = item.SubtitleTracks;
        Log($"subtitle tracks: count={subtitleTracks.Count} selected={subtitleTracks.SelectedIndex} " +
            $"labels=[{DescribeTracks(subtitleTracks)}]");
    }

    private static async Task SwitchAudioAsync(FFmpegMediaPlayer player, FFmpegPlaybackItem item)
    {
        FFmpegAudioTrackList tracks = item.AudioTracks;
        if (tracks.Count < 2)
        {
            Log("audio switch: skipped, fewer than two tracks");
            return;
        }

        int target = tracks.SelectedIndex == 0 ? 1 : 0;
        int demuxerBefore = item.FFmpegSource?.CurrentAudioStream?.StreamIndex ?? -1;
        Log($"audio switch: before list={tracks.SelectedIndex} demuxer={demuxerBefore} " +
            $"active=[{DescribeAudioActivity(item)}]");

        tracks.SelectedIndex = target;
        await Task.Delay(SettleDelay);

        Log($"audio switch: +{SettleDelay.TotalMilliseconds:0}ms list={tracks.SelectedIndex} (target {target}) " +
            $"item={item.MediaPlaybackItem?.AudioTracks.SelectedIndex ?? -1} " +
            $"demuxer={item.FFmpegSource?.CurrentAudioStream?.StreamIndex ?? -1} " +
            $"active=[{DescribeAudioActivity(item)}] label='{tracks[target].Label}' " +
            $"state={player.PlaybackState} pos={player.Position}");

        // The demuxer re-opens the selected stream asynchronously; confirm it settles.
        await Task.Delay(1500);
        Log($"audio switch: +{SettleDelay.TotalMilliseconds + 1500:0}ms " +
            $"demuxer={item.FFmpegSource?.CurrentAudioStream?.StreamIndex ?? -1} " +
            $"active=[{DescribeAudioActivity(item)}] pos={player.Position} state={player.PlaybackState}");
    }

    private static string DescribeAudioActivity(FFmpegPlaybackItem item)
    {
        FFmpegMediaSource? source = item.FFmpegSource;
        if (source is null) return "no source";

        string result = string.Empty;
        foreach (AudioStreamInfo audio in source.AudioStreams)
        {
            if (result.Length > 0) result += " | ";
            result += $"{audio.StreamIndex}:{(audio.IsActive ? "active" : "idle")}";
        }

        return result.Length == 0 ? "empty" : result;
    }

    private static async Task ExerciseAudioDelayAsync(FFmpegMediaPlayer player)
    {
        // A track switch re-opens the audio stream; the delay has to be re-applied to the new
        // one. This exercises that path and reports whether playback survives it.
        player.AudioDelay = 200;
        await Task.Delay(600);
        Log($"audio delay: requested=200 actual={player.AudioDelay} state={player.PlaybackState} pos={player.Position}");

        player.AudioDelay = 0;
        await Task.Delay(300);
        Log($"audio delay: reset actual={player.AudioDelay} state={player.PlaybackState} pos={player.Position}");
    }

    private static async Task SelectEmbeddedSubtitleAsync(FFmpegPlaybackItem item)
    {
        FFmpegSubtitleTrackList tracks = item.SubtitleTracks;
        if (tracks.Count == 0)
        {
            Log("subtitle select: skipped, no embedded subtitles");
            return;
        }

        tracks.SelectedIndex = 0;
        await Task.Delay(600);

        Log($"subtitle select: selected={tracks.SelectedIndex} label='{tracks[0].Label}' " +
            $"presentation=[{DescribePresentationModes(item)}]");
    }

    private static async Task AddExternalSubtitleAsync(FFmpegMediaPlayer player, FFmpegPlaybackItem item)
    {
        string? path = Environment.GetEnvironmentVariable("SCREENBOX_PROBE_SUBTITLE");
        if (string.IsNullOrWhiteSpace(path))
        {
            Log("external subtitle: skipped, SCREENBOX_PROBE_SUBTITLE not set");
            return;
        }

        int before = item.FFmpegSource?.SubtitleStreams.Count ?? 0;
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        player.AddSubtitle(file, select: true);
        await Task.Delay(1800);

        int after = item.FFmpegSource?.SubtitleStreams.Count ?? 0;
        FFmpegSubtitleTrackList tracks = item.SubtitleTracks;
        Log($"external subtitle: demuxer streams {before}->{after} list count={tracks.Count} " +
            $"selected={tracks.SelectedIndex} labels=[{DescribeTracks(tracks)}] " +
            $"presentation=[{DescribePresentationModes(item)}]");
    }

    private static async Task SeekAsync(FFmpegMediaPlayer player, TimeSpan target, string label)
    {
        TimeSpan before = player.Position;
        Stopwatch clock = Stopwatch.StartNew();
        player.Position = target;

        // The session reports the requested position immediately, so the number to measure is
        // how long until decoding actually resumes and the clock starts moving again. With
        // FastSeek that resumes at the nearest keyframe, not necessarily at the target.
        TimeSpan reported = player.Position;
        TimeSpan last = reported;
        long resumeMs = -1;
        while (clock.Elapsed < TimeSpan.FromSeconds(4))
        {
            await Task.Delay(15);
            TimeSpan now = player.Position;
            if (now != last)
            {
                resumeMs = clock.ElapsedMilliseconds;
                break;
            }

            last = now;
        }

        TimeSpan resumed = player.Position;
        Log($"seek {label}: from={before} target={target} reported={reported} resumed_at={resumed} " +
            $"resume={resumeMs}ms snap={(resumed - target).TotalMilliseconds:0}ms state={player.PlaybackState}");

        await Task.Delay(500);
        Log($"seek {label}: +500ms pos={player.Position} state={player.PlaybackState}");
    }

    private static async Task ChangeRateAsync(FFmpegMediaPlayer player, double rate)
    {
        player.PlaybackRate = rate;
        await Task.Delay(700);
        Log($"rate: requested={rate} actual={player.PlaybackRate} pos={player.Position} " +
            $"state={player.PlaybackState}");
    }

    private static string DescribeTracks<T>(SingleSelectTrackList<T> tracks) where T : IMediaTrack
    {
        string result = string.Empty;
        for (int i = 0; i < tracks.Count; i++)
        {
            if (i > 0) result += " | ";
            result += tracks[i].Label;
        }

        return result;
    }

    private static string DescribePresentationModes(FFmpegPlaybackItem item)
    {
        MediaPlaybackItem? playbackItem = item.MediaPlaybackItem;
        if (playbackItem is null) return "no playback item";

        string result = string.Empty;
        try
        {
            for (int i = 0; i < playbackItem.TimedMetadataTracks.Count; i++)
            {
                if (i > 0) result += " | ";
                result += playbackItem.TimedMetadataTracks.GetPresentationMode((uint)i);
            }
        }
        catch (Exception ex)
        {
            return "unavailable: " + ex.Message;
        }

        return result.Length == 0 ? "empty" : result;
    }

    private static void Log(string message) => PlaybackTrace.Write("probe: " + message);
}
