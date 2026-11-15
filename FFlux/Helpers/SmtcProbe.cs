using System;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Playback;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only (<c>FFLUX_SMTC_PROBE=1</c>): reports which route to the Windows system
/// media transport controls actually works in an unpackaged WinUI 3 process.
/// </summary>
/// <remarks>
/// The three routes differ in availability:
/// <list type="bullet">
/// <item><c>SystemMediaTransportControls.GetForCurrentView()</c> is the UWP API and needs a
/// CoreApplication view backed by package identity.</item>
/// <item><c>MediaPlayer.SystemMediaTransportControls</c> is the instance the player owns, which
/// is what a desktop app is supposed to use.</item>
/// <item><c>MediaPlayer.CommandManager</c> is a newer, higher level switch that also lights up
/// the system flyout but takes over button handling.</item>
/// </list>
/// </remarks>
internal static class SmtcProbe
{
    private static MediaPlayer? _retainedPlayer;

    private static void Log(string message) => StartupErrorLog.Write("SMTC probe: " + message);

    public static void Run()
    {
        Log("--- start ---");
        Log($"Process: {Environment.ProcessPath}");

        ProbeLegacyRoute();
        ProbeMediaPlayerRoute();
        Log("--- end ---");
    }

    private static void ProbeLegacyRoute()
    {
        try
        {
            SystemMediaTransportControls controls = SystemMediaTransportControls.GetForCurrentView();
            Log($"GetForCurrentView: OK, IsEnabled={controls.IsEnabled}");
        }
        catch (Exception ex)
        {
            Log($"GetForCurrentView: threw {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}");
        }
    }

    private static void ProbeMediaPlayerRoute()
    {
        MediaPlayer? player = null;
        try
        {
            player = new MediaPlayer();
            Log("MediaPlayer: created");

            SystemMediaTransportControls? controls = player.SystemMediaTransportControls;
            if (controls is null)
            {
                Log("MediaPlayer.SystemMediaTransportControls: null");
            }
            else
            {
                Log($"MediaPlayer.SystemMediaTransportControls: OK, IsEnabled={controls.IsEnabled}");
                try
                {
                    controls.IsEnabled = true;
                    controls.IsPlayEnabled = true;
                    controls.IsPauseEnabled = true;
                    controls.IsStopEnabled = true;
                    controls.IsNextEnabled = true;
                    controls.IsPreviousEnabled = true;
                    controls.DisplayUpdater.Type = MediaPlaybackType.Music;
                    controls.DisplayUpdater.MusicProperties.Title = "FFlux SMTC probe";
                    controls.DisplayUpdater.MusicProperties.Artist = "probe";
                    controls.DisplayUpdater.Update();
                    controls.PlaybackStatus = MediaPlaybackStatus.Playing;
                    Log($"SMTC publish: OK, status={controls.PlaybackStatus}," +
                        $" buttons p/p/s/n/prev={controls.IsPlayEnabled}/{controls.IsPauseEnabled}/" +
                        $"{controls.IsStopEnabled}/{controls.IsNextEnabled}/{controls.IsPreviousEnabled}");

                    // Read back immediately: if the values do not survive the round trip, the
                    // property bag is not a reliable oracle and any "Title is empty" finding
                    // below has to be taken with a grain of salt.
                    Log($"SMTC readback: Type={controls.DisplayUpdater.Type}," +
                        $" Title='{controls.DisplayUpdater.MusicProperties.Title}'");
                }
                catch (Exception ex)
                {
                    Log($"SMTC publish: threw {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}");
                }
            }

            MediaPlaybackCommandManager? manager = player.CommandManager;
            if (manager is null)
            {
                Log("CommandManager: null");
            }
            else
            {
                Log($"CommandManager: present, IsEnabled={manager.IsEnabled}");
                try
                {
                    manager.IsEnabled = true;
                    Log($"CommandManager: IsEnabled set, now={manager.IsEnabled}");
                }
                catch (Exception ex)
                {
                    Log($"CommandManager.IsEnabled: threw {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"MediaPlayer route: threw {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}");
        }

        // Deliberately not disposed: disposing a MediaPlayer closes its SMTC session, so the
        // system flyout would vanish before anyone could look at it. Hold a reference so the
        // garbage collector cannot do the same thing behind our back.
        _retainedPlayer = player;
    }

    public static async Task RunAsync()
    {
        Run();

        // Give playback a chance to start (the probe is usually combined with a media file on
        // the command line), then report what the application's own service ended up with.
        await Task.Delay(TimeSpan.FromSeconds(10));
        await ReportServiceStateAsync();
    }

    /// <summary>
    /// Reports the session the application actually uses, as opposed to the throwaway player
    /// created by <see cref="Run"/>. Reads through the DI container so the instance checked is
    /// the one playback is wired to.
    /// </summary>
    private static async Task ReportServiceStateAsync()
    {
        try
        {
            var ioc = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default;
            var service = ioc.GetService<Screenbox.Core.Services.ISystemMediaTransportControlsService>();
            if (service is null)
            {
                Log("service: not resolvable from DI");
                return;
            }

            SystemMediaTransportControls? controls = service.TransportControls;
            if (controls is null)
            {
                Log("service.TransportControls: null (integration disabled)");
                return;
            }

            // Distinguish "never called" from "called but the values did not stick": snapshot
            // first (this is the result of the automatic queue subscription), then publish the
            // current item explicitly and snapshot again.
            Log("service before explicit update: " + Snapshot(controls));

            var queue = ioc.GetService<Screenbox.Core.Contexts.PlayQueueContext>();
            Screenbox.Core.ViewModels.MediaViewModel? item = queue?.CurrentItem;
            Log($"service: current queue item = {(item is null ? "null" : item.Name)}");
            if (item is not null)
            {
                try
                {
                    await service.UpdateTransportControlsDisplayAsync(item);
                    Log("service: explicit display update returned");
                }
                catch (Exception ex)
                {
                    Log($"service: explicit display update threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            Log("service after explicit update: " + Snapshot(controls));
        }
        catch (Exception ex)
        {
            Log($"service state: threw {ex.GetType().Name} (0x{ex.HResult:X8}) {ex.Message}");
        }
    }

    private static string Snapshot(SystemMediaTransportControls controls)
    {
        var updater = controls.DisplayUpdater;
        string title = updater.Type switch
        {
            MediaPlaybackType.Music => updater.MusicProperties.Title ?? string.Empty,
            MediaPlaybackType.Video => updater.VideoProperties.Title ?? string.Empty,
            _ => string.Empty
        };

        return $"IsEnabled={controls.IsEnabled}, Status={controls.PlaybackStatus}, Type={updater.Type}, " +
               $"Title='{title}', Artist='{updater.MusicProperties.Artist}', " +
               $"HasThumbnail={updater.Thumbnail is not null}";
    }
}
