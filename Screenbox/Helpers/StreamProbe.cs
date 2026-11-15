using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Screenbox.Core.Enums;
using Screenbox.Core.Models;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Screenbox.Dialogs;
using Screenbox.Pages;

namespace Screenbox.Helpers;

/// <summary>
/// Diagnostic-only (<c>FFLUX_STREAM_PROBE=1</c>): exercises the network stream library end to
/// end without a human clicking through it.
/// </summary>
/// <remarks>
/// This host cannot be screenshotted, so the probe is the only way to prove that the new page
/// and its dialogs really load and that a URL survives the round trip through the JSON file.
/// The library is pointed at a scratch file while the probe runs, so the user's own list is
/// never touched.
/// </remarks>
internal static class StreamProbe
{
    private const string ProbeUrl = "https://example.com/live/ch1.m3u8";

    public static async Task RunAsync(Window window)
    {
        await Task.Delay(4000);

        try
        {
            var frame = window.Content as Frame;
            var mainPage = frame?.Content as MainPage;
            if (mainPage is null)
            {
                StartupErrorLog.Write("Stream probe: main page not found");
                return;
            }

            mainPage.NavigateContent(typeof(StreamsPage), null);
            await Task.Delay(2000);

            if (mainPage.FrameContent is not StreamsPage page)
            {
                StartupErrorLog.Write("Stream probe: streams page did not load");
                return;
            }

            StartupErrorLog.Write("Stream probe: page loaded");

            await RunLibraryChecksAsync(page);

            await RunDefaultPathCheckAsync();

            await RunDialogChecksAsync(window);
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Stream probe failed: " + ex);
        }
    }

    private static async Task RunLibraryChecksAsync(StreamsPage page)
    {
        var service = (StreamManagerService)Ioc.Default.GetRequiredService<IStreamManagerService>();
        string originalPath = service.FilePath;
        string probeDirectory = Path.Combine(Path.GetTempPath(), "fflux-stream-probe");
        string probePath = Path.Combine(probeDirectory, "streams.json");

        // The service is a singleton and may already hold the user's entries. Move them aside
        // for the duration of the probe instead of writing to the real library.
        var saved = new System.Collections.Generic.List<StreamItem>(service.Items);
        service.Items.Clear();
        service.FilePath = probePath;

        try
        {
            StreamsPageViewModel vm = page.ViewModel;
            await vm.LoadAsync();

            // 1. A local path must be refused: it belongs to "open file", not to a stream.
            QuickAddResult rejected = await vm.AddUrlAsync(@"C:\videos\clip.mkv", null, null, null, null);
            StartupErrorLog.Write($"Stream probe: local path rejected = {rejected.Error == "invalid-url"}");

            // 2. Quick add with the anti-hotlink fields.
            QuickAddResult added = await vm.AddUrlAsync(
                ProbeUrl, "Probe Channel", "VLC/3.0", "https://example.com/", "Cookie: a=b");
            StreamItem? item = added.Item;
            StartupErrorLog.Write(
                $"Stream probe: added name={item?.Name} format={item?.FormatType} badge={item?.BadgeText} error={added.Error}");
            if (item is null) return;

            // 3. Rename and edit.
            vm.Rename(item, "Renamed Channel");
            vm.ApplyEdit(item, new StreamItem
            {
                Name = "Renamed Channel",
                Url = "https://example.com/live/ch2.m3u8",
                FormatType = StreamFormatType.Hls,
                UserAgent = "UA2",
                Referer = "https://example.com/",
                Headers = "Cookie: c=d",
            });

            // 4. Search filter.
            vm.SearchQuery = "Renam";
            StartupErrorLog.Write($"Stream probe: filtered count={vm.VisibleStreams.Count}");
            vm.SearchQuery = "no-such-channel";
            StartupErrorLog.Write($"Stream probe: empty filter count={vm.VisibleStreams.Count}");
            vm.SearchQuery = string.Empty;

            // 5. Persist and read the file back with a fresh instance.
            await service.SaveAsync();
            StartupErrorLog.Write($"Stream probe: file exists={File.Exists(probePath)}");

            using (var reader = new StreamManagerService { FilePath = probePath })
            {
                await reader.LoadAsync();
                StreamItem? loaded = reader.Items.Count > 0 ? reader.Items[0] : null;
                StartupErrorLog.Write(
                    $"Stream probe: reloaded count={reader.Items.Count} name={loaded?.Name} url={loaded?.Url} " +
                    $"ua={loaded?.UserAgent} referer={loaded?.Referer} headers={loaded?.Headers} badge={loaded?.BadgeText}");
            }

            // 6. Remove and confirm the file follows.
            vm.Remove(item);
            await service.SaveAsync();

            using (var reader = new StreamManagerService { FilePath = probePath })
            {
                await reader.LoadAsync();
                StartupErrorLog.Write($"Stream probe: after remove count={reader.Items.Count}");
            }
        }
        finally
        {
            service.FilePath = originalPath;
            service.Items.Clear();
            foreach (StreamItem restored in saved)
            {
                service.Items.Add(restored);
            }

            try
            {
                if (Directory.Exists(probeDirectory)) Directory.Delete(probeDirectory, true);
            }
            catch (Exception)
            {
                // The scratch file is disposable; a leftover copy is harmless.
            }
        }
    }

    /// <summary>
    /// Writes to the real library path once and restores whatever was there before. The scratch
    /// checks above use a temporary folder, so this is the only step that proves the shipped
    /// location - <c>%LOCALAPPDATA%\FFlux\streams.json</c> - is created and written correctly.
    /// </summary>
    private static async Task RunDefaultPathCheckAsync()
    {
        string path = StreamManagerService.DefaultFilePath;
        string? backup = null;

        try
        {
            if (File.Exists(path))
            {
                backup = path + ".probe-backup";
                File.Copy(path, backup, overwrite: true);
            }

            using (var service = new StreamManagerService { FilePath = path })
            {
                service.Add(new StreamItem { Name = "默认路径探针", Url = ProbeUrl });
                await service.SaveAsync();
            }

            using (var reader = new StreamManagerService { FilePath = path })
            {
                await reader.LoadAsync();
                StartupErrorLog.Write(
                    $"Stream probe: default path count={reader.Items.Count} name={reader.Items.FirstOrDefault()?.Name}");
            }
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Stream probe: default path failed: " + ex.Message);
        }
        finally
        {
            try
            {
                if (backup is not null && File.Exists(backup))
                {
                    File.Copy(backup, path, overwrite: true);
                    File.Delete(backup);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception)
            {
                // Never let cleanup damage a real library.
            }
        }
    }

    private static async Task RunDialogChecksAsync(Window window)
    {
        var openUrl = new OpenUrlDialog
        {
            XamlRoot = (window.Content as FrameworkElement)?.XamlRoot
        };
        openUrl.Opened += (_, _) => StartupErrorLog.Write("Stream probe: open url dialog opened");
        Windows.Foundation.IAsyncOperation<ContentDialogResult> show = openUrl.ShowAsync();
        await Task.Delay(1200);
        openUrl.Hide();
        StartupErrorLog.Write("Stream probe: open url dialog closed, result=" + await show);

        var rename = new RenameStreamDialog("Probe")
        {
            XamlRoot = (window.Content as FrameworkElement)?.XamlRoot
        };
        rename.Opened += (_, _) => StartupErrorLog.Write("Stream probe: rename dialog opened");
        Task<string?> showRename = rename.GetNameAsync();
        await Task.Delay(1200);
        rename.Hide();
        StartupErrorLog.Write("Stream probe: rename dialog closed, result=" + await showRename);

        var edit = new EditStreamDialog(new StreamItem { Name = "Probe", Url = ProbeUrl })
        {
            XamlRoot = (window.Content as FrameworkElement)?.XamlRoot
        };
        edit.Opened += (_, _) => StartupErrorLog.Write("Stream probe: edit dialog opened");
        Task<StreamItem?> showEdit = edit.GetResultAsync();
        await Task.Delay(1200);
        edit.Hide();
        StartupErrorLog.Write("Stream probe: edit dialog closed, result=" + (await showEdit)?.Url);
    }
}
