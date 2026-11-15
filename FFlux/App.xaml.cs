using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.WinUI.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Screenbox.Core;
using Screenbox.Core.Contexts;
using Screenbox.Core.Helpers;
using Screenbox.Core.Messages;
using Screenbox.Core.Services;
using Screenbox.Core.ViewModels;
using Screenbox.Helpers;
using Screenbox.Lively;
using Screenbox.Pages;
using Screenbox.Services;
using Sentry;
using Sentry.Protocol;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.ApplicationModel.Core;
using Windows.Storage;
using Windows.Storage.Search;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Screenbox;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
sealed partial class App : Application
{
    private readonly IServiceProvider _services;
    private readonly ILogger<App> _logger;
    private readonly ITelemetryService _telemetryService;
    private Window? _window;
    private WindowTitleController? _windowTitle;
    private IAppDispatcherTimer? _heartbeatTimer;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        // Move the pre-rename data folder (%LOCALAPPDATA%\Screenbox) into the FFlux folder
        // before anything reads settings, the database, or the thumbnail cache.
        AppDataMigration.MigrationResult migration = AppDataMigration.Run();
        StartupErrorLog.Write($"App ctor; data migration: {migration.Message} (migrated={migration.Migrated} skipped={migration.Skipped} failed={migration.Failed})");
        // Initialize Sentry SDK and immediately register error handlers before XAML is initialized
        _ = InitializeSentrySdk();
        UnhandledException += App_UnhandledException;
        // CoreApplication.UnhandledErrorDetected needs a UWP CoreApplication view, which an
        // unpackaged WinUI 3 process does not have. Application.UnhandledException above is
        // the supported hook; the helper below is retained for a future packaged build.
        GlobalizationHelper.SyncCurrentCultureWithAppLanguage();

        InitializeComponent();

        // WinUI 3 has no Xbox support and no UWP-only Application properties
        // (RequiresPointerMode, Reveal focus, HighContrastAdjustment); system defaults apply.

        _services = ConfigureServices();
        CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.ConfigureServices(_services);

        _logger = _services.GetRequiredService<ILogger<App>>();
        _telemetryService = _services.GetRequiredService<ITelemetryService>();
        StartupErrorLog.Write("App ctor done");

        // WinUI 3 has no Application.Suspending event. State is flushed on window close
        // instead; OnSuspending stays as a reusable helper until the lifecycle rewrite.
        // Suspending += OnSuspending;
    }

    [SecurityCritical]
    private void CoreApplication_UnhandledErrorDetected(object? sender, UnhandledErrorDetectedEventArgs e)
    {
        try
        {
            e.UnhandledError.Propagate();
        }
        catch (Exception ex)
        {
            // Tell Sentry this was an unhandled exception
            ex.Data[Mechanism.HandledKey] = false;
            ex.Data[Mechanism.MechanismKey] = "CoreApplication.UnhandledErrorDetected";

            // Capture the exception
            SentrySdk.CaptureException(ex);

            // Flush the event immediately
            SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();

            throw;
        }
    }

    [SecurityCritical]
    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // Get a reference to the exception, because the Exception property is cleared when accessed.
        var exception = e.Exception;
        if (exception != null)
        {
            // Tell Sentry this was an unhandled exception
            exception.Data[Mechanism.HandledKey] = false;
            exception.Data[Mechanism.MechanismKey] = "Application.UnhandledException";

            // Capture the exception
            StartupErrorLog.Write("Unhandled exception: " + exception);
            SentrySdk.CaptureException(exception);

            // Never block the UI dispatcher thread inside an exception callback: a
            // synchronous flush here deadlocks the message loop and the process is
            // then torn down by CoreMessaging as a stowed exception.
            _ = SentrySdk.FlushAsync(TimeSpan.FromSeconds(2));

            // Report and keep running. Without this the XAML framework rethrows and
            // terminates the app even though the error was already captured.
            e.Handled = true;
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        ServiceCollection services = new();
        ServiceHelpers.PopulateCoreServices(services);
        services.AddLivelyWallpaperServices();
        services.AddSingleton<ITelemetryService, SentryTelemetryService>();
        services.AddLogging(builder =>
        {
#if DEBUG
            builder.AddDebug();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
#endif
            builder.AddSentry(options =>
            {
                ConfigureSentryOptions(options);
                options.InitializeSdk = false;
                options.MinimumEventLevel = Microsoft.Extensions.Logging.LogLevel.Error;
                options.MinimumBreadcrumbLevel = Microsoft.Extensions.Logging.LogLevel.Information;
            });
        });

        // Services
        services.AddSingleton<INavigationService, NavigationService>(_ => new NavigationService(
            new KeyValuePair<Type, Type>(typeof(HomePageViewModel), typeof(HomePage)),
            new KeyValuePair<Type, Type>(typeof(PlaylistsPageViewModel), typeof(PlaylistsPage)),
            new KeyValuePair<Type, Type>(typeof(PlaylistDetailsPageViewModel), typeof(PlaylistDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(VideosPageViewModel), typeof(VideosPage)),
            new KeyValuePair<Type, Type>(typeof(AllVideosPageViewModel), typeof(AllVideosPage)),
            new KeyValuePair<Type, Type>(typeof(MusicPageViewModel), typeof(MusicPage)),
            new KeyValuePair<Type, Type>(typeof(SongsPageViewModel), typeof(SongsPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistsPageViewModel), typeof(ArtistsPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumsPageViewModel), typeof(AlbumsPage)),
            new KeyValuePair<Type, Type>(typeof(NetworkPageViewModel), typeof(NetworkPage)),
            new KeyValuePair<Type, Type>(typeof(StreamsPageViewModel), typeof(StreamsPage)),
            new KeyValuePair<Type, Type>(typeof(PlayQueuePageViewModel), typeof(PlayQueuePage)),
            new KeyValuePair<Type, Type>(typeof(SettingsPageViewModel), typeof(SettingsPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumDetailsPageViewModel), typeof(AlbumDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistDetailsPageViewModel), typeof(ArtistDetailsPage)),
            new KeyValuePair<Type, Type>(typeof(SearchResultPageViewModel), typeof(SearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(ArtistSearchResultPageViewModel), typeof(ArtistSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(AlbumSearchResultPageViewModel), typeof(AlbumSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(SongSearchResultPageViewModel), typeof(SongSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(VideoSearchResultPageViewModel), typeof(VideoSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(PlaylistSearchResultPageViewModel), typeof(PlaylistSearchResultPage)),
            new KeyValuePair<Type, Type>(typeof(FolderViewPageViewModel), typeof(FolderViewPage)),
            new KeyValuePair<Type, Type>(typeof(FolderListViewPageViewModel), typeof(FolderListViewPage))
        ));

        return services.BuildServiceProvider();
    }

    private static IDisposable InitializeSentrySdk()
    {
        var initRef = SentrySdk.Init(options => ConfigureSentryOptions(options));

        SentrySdk.ConfigureScope(scope =>
        {
            scope.SetTag("device_family", DeviceInfoHelper.DeviceFamily);
        });

        return initRef;
    }

    private static void ConfigureSentryOptions(SentryOptions options)
    {
        options.Dsn = Secrets.SentryDsn;
        options.SampleRate = 1.0f;
        options.IsGlobalModeEnabled = true;
        options.AutoSessionTracking = true;
        // Unpackaged apps have no Package.Current; use the assembly version instead.
        options.Release = $"screenbox@{typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";
    }

    private void SetMinWindowSize()
    {
        //var view = ApplicationView.GetForCurrentView();
        //view.SetPreferredMinSize(new Size(480, 270));
    }

    // WinUI 3 / unpackaged has no Application.OnFileActivated override. File activation has
    // to be routed through Program.cs command-line parsing in the lifecycle rewrite; this
    // helper keeps the existing playback logic reusable until that work lands.
    private async void HandleFileActivation(FileActivatedEventArgs args)
    {
        _logger.LogInformation("File activation started. {PreviousExecutionState}", args.PreviousExecutionState);

        Frame rootFrame = InitRootFrame();
        if (rootFrame.Content is not MainPage)
        {
            rootFrame.Navigate(typeof(MainPage), true);
        }

        _window?.Activate();
        var queueService = _services.GetRequiredService<IQueueService>();
        var generalSettings = _services.GetRequiredService<GeneralSettingsContext>();

        StorageFileQueryResult? neighboringFilesQuery = null;
        try
        {
            neighboringFilesQuery = args.NeighboringFilesQuery;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve NeighboringFilesQuery during file activation.");
        }

        await WeakReferenceMessenger.Default.PlayFilesAsync(
            args.Files,
            queueService,
            generalSettings.EnqueueAllFilesInFolder,
            neighboringFilesQuery);
    }

    /// <summary>
    /// Invoked when the application is launched normally by the end user.  Other entry points
    /// will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="e">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs e)
    {
        _logger.LogInformation("App launched.");
        StartupErrorLog.Write("OnLaunched entered");

        // Application.Start does not create a window for a WinUI 3 desktop app. Register
        // it before InitRootFrame assigns Content: TitleBar, state triggers and the
        // navigation service all resolve the window while the XAML tree is built.
        if (_window is null)
        {
            _window = new Window();
            WindowAccessor.Register(_window);
            Screenbox.UI.Controls.TitleBar.HostWindow = _window;
            // An unpackaged window has no ApplicationView, so its caption has to be set
            // explicitly or it stays "WinUI Desktop" everywhere the shell shows it.
            // Held in a field: the messenger keeps only a weak reference to the recipient.
            _windowTitle = new WindowTitleController(_window);
            _windowTitle.Attach();
            _window.Closed += (_, _) => StartupErrorLog.Write("Window closed");
            try
            {
                _window.AppWindow.Destroying += (_, _) => StartupErrorLog.Write("AppWindow destroying");
            }
            catch (Exception ex)
            {
                StartupErrorLog.Write("AppWindow hook unavailable: " + ex.Message);
            }
            StartupErrorLog.Write($"Window created (HWND=0x{AppWindowInterop.WindowHandle.ToInt64():X})");
        }

        Frame rootFrame = InitRootFrame();
        StartupErrorLog.Write("Root frame ready");

        if (rootFrame.Content == null)
        {
            SetMinWindowSize();
            rootFrame.Navigate(typeof(MainPage));
        }

        _ = StartupInitAsync();
        _window.Activate();
        StartupErrorLog.Write("Window activated");
        if (Environment.GetEnvironmentVariable("FFLUX_STARTUP_TRACE") == "1")
        {
            StartHeartbeat();
        }
        if (LaunchArguments.Files.Count > 0)
        {
            _ = OpenCommandLineFilesAsync();
        }
        if (Environment.GetEnvironmentVariable("FFLUX_PICKER_PROBE") == "1")
        {
            _ = ProbePickerInteropAsync();
        }
        if (Environment.GetEnvironmentVariable("FFLUX_DIALOG_PROBE") == "1")
        {
            _ = ProbePropertiesDialogAsync();
        }
        if (Environment.GetEnvironmentVariable("FFLUX_STREAM_PROBE") == "1")
        {
            _ = Screenbox.Helpers.StreamProbe.RunAsync(_window);
        }
        if (Environment.GetEnvironmentVariable("FFLUX_LIBRARY_PROBE") == "1")
        {
            _ = Screenbox.Helpers.LibraryProbe.RunAsync();
        }

#if DEBUG
        if (System.Diagnostics.Debugger.IsAttached)
        {
            //DebugSettings.EnableFrameRateCounter = true;
            //DebugSettings.EnableRedrawRegions = true;
            //DebugSettings.FailFastOnErrors = true;
            //DebugSettings.IsBindingTracingEnabled = true;
            //DebugSettings.IsOverdrawHeatMapEnabled = true;
            //DebugSettings.IsTextPerformanceVisualizationEnabled = true;
        }
#endif
    }

    /// <summary>
    /// A short-lived heartbeat tells us whether the WinUI dispatcher loop is still
    /// processing work. Heartbeats that keep arriving while the process dies prove
    /// the termination came from outside the managed message loop. Opt-in via
    /// FFLUX_STARTUP_TRACE=1 so normal launches stay quiet.
    /// </summary>
    private void StartHeartbeat()
    {
        IAppDispatcherTimer timer = AppDispatcher.Current.CreateTimer();
        _heartbeatTimer = timer;
        int ticks = 0;
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) =>
        {
            if (++ticks > 20)
            {
                timer.Stop();
                return;
            }
            StartupErrorLog.Write("heartbeat " + ticks);
        };
        timer.Start();
    }

    /// <summary>
    /// Invoked when Navigation to a certain page fails
    /// </summary>
    /// <param name="sender">The Frame which failed navigation</param>
    /// <param name="e">Details about the navigation failure</param>
    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        // Persist the original parser/COM error before replacing it: the XAML loader
        // otherwise reports only the opaque "Failed to load Page" exception.
        StartupErrorLog.Write("Navigation to " + e.SourcePageType.FullName + " failed: " + e.Exception);
        throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
    }

    /// <summary>
    /// Invoked when application execution is being suspended.  Application state is saved
    /// without knowing whether the application will be terminated or resumed with the contents
    /// of memory still intact.
    /// </summary>
    /// <param name="sender">The source of the suspend request.</param>
    /// <param name="e">Details about the suspend request.</param>
    private async void OnSuspending(object sender, SuspendingEventArgs e)
    {
        SuspendingDeferral deferral = e.SuspendingOperation.GetDeferral();
        try
        {
            _logger.LogInformation("Application is suspending.");
            var tasks = WeakReferenceMessenger.Default.Send<SuspendingMessage>().Responses.ToList();
            if (_telemetryService.IsEnabled)
            {
                tasks.Add(_telemetryService.FlushAsync());
            }

            await Task.WhenAll(tasks);
        }
        catch (Exception)
        {
            // pass
        }
        finally
        {
            deferral.Complete();
        }
    }

    /// <summary>
    /// Diagnostic-only (FFLUX_DIALOG_PROBE=1): opens the properties dialog for whatever is
    /// playing and closes it again. This host cannot be screenshotted, so a successful open is
    /// how the dialog's XAML, the property rows and the panel layout get exercised.
    /// </summary>
    private async Task ProbePropertiesDialogAsync()
    {
        await Task.Delay(6000);
        try
        {
            var queue = _services.GetRequiredService<PlayQueueContext>();
            MediaViewModel? media = queue.CurrentItem;
            StartupErrorLog.Write("Dialog probe: current media = " + (media?.Name ?? "<none>"));

            var viewModel = _services.GetRequiredService<PropertyViewModel>();
            if (media != null)
            {
                await viewModel.LoadPropertiesAsync(media);
                StartupErrorLog.Write(
                    $"Dialog probe: rows media={viewModel.MediaProperties.Count} video={viewModel.VideoProperties.Count} " +
                    $"audio={viewModel.AudioProperties.Count} tracks={viewModel.TrackProperties.Count} file={viewModel.FileProperties.Count}");
            }

            await ProbeDialogStepsAsync(media);

            var dialog = new Dialogs.PropertiesDialog { Media = media };
            dialog.XamlRoot = _window?.Content?.XamlRoot;
            dialog.Opened += (_, _) => StartupErrorLog.Write("Dialog probe: opened");
            Windows.Foundation.IAsyncOperation<ContentDialogResult> show = dialog.ShowAsync();
            await Task.Delay(1500);
            dialog.Hide();
            StartupErrorLog.Write("Dialog probe: closed, result=" + (await show));
        }
        catch (Exception ex)
        {
            int depth = 0;
            for (Exception? e = ex; e != null; e = e.InnerException, depth++)
            {
                StartupErrorLog.Write($"Dialog probe FAILED [{depth}] {e.GetType().FullName} hr=0x{e.HResult:X8}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Diagnostic-only: measures the property panel off-tree so a layout failure is reported as a
    /// catchable managed exception instead of killing the process through the XAML host.
    ///
    /// A bare <see cref="ContentDialog"/> is opened first: if that dies, dialogs are broken in
    /// general and the panel is innocent.
    /// </summary>
    private async Task ProbeDialogStepsAsync(MediaViewModel? media)
    {
        XamlRoot? xamlRoot = _window?.Content?.XamlRoot;
        StartupErrorLog.Write($"Dialog steps: XamlRoot={(xamlRoot == null ? "null" : "ok")} window={(_window == null ? "null" : "ok")}");

        try
        {
            var bare = new ContentDialog
            {
                Title = "probe",
                Content = new TextBlock { Text = "hello" },
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
            };
            Windows.Foundation.IAsyncOperation<ContentDialogResult> show = bare.ShowAsync();
            await Task.Delay(800);
            bare.Hide();
            StartupErrorLog.Write("Dialog steps: 1 bare ContentDialog OK, result=" + (await show));
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write($"Dialog steps: 1 bare ContentDialog FAILED {ex.GetType().Name} hr=0x{ex.HResult:X8}: {ex.Message}");
        }

        try
        {
            var panel = new Controls.PropertyPanel();
            panel.Update(media);
            panel.Root.Measure(new Windows.Foundation.Size(420, 900));
            panel.Root.Arrange(new Windows.Foundation.Rect(0, 0, 420, 900));
            StartupErrorLog.Write(
                $"Dialog steps: 2 PropertyPanel layout OK, desired={panel.Root.DesiredSize.Width}x{panel.Root.DesiredSize.Height}");

            // Confirm the new technical labels really resolve from resources.pri rather than
            // falling back to the enum name (which would silently mean a stale resource index).
            foreach (Core.Enums.MediaProperty property in new[]
                     {
                         Core.Enums.MediaProperty.VideoCodec,
                         Core.Enums.MediaProperty.FrameRate,
                         Core.Enums.MediaProperty.SampleRate,
                         Core.Enums.MediaProperty.Channels,
                         Core.Enums.MediaProperty.Container,
                         Core.Enums.MediaProperty.ChapterCount,
                     })
            {
                StartupErrorLog.Write(
                    $"Dialog steps: label {property} = '{Helpers.ItemLabelHelper.GetMediaPropertyResourceString(property)}'");
            }
        }
        catch (Exception ex)
        {
            int depth = 0;
            for (Exception? e = ex; e != null; e = e.InnerException, depth++)
            {
                StartupErrorLog.Write($"Dialog steps: 2 PropertyPanel FAILED [{depth}] {e.GetType().FullName} hr=0x{e.HResult:X8}: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Diagnostic-only (FFLUX_PICKER_PROBE=1): starts a file picker without
    /// awaiting user input so a missing IInitializeWithWindow binding shows up as
    /// 0x80070578 in the startup log instead of only when a user clicks "Open".
    /// </summary>
    private async Task ProbePickerInteropAsync()
    {
        await Task.Delay(1500);
        try
        {
            StartupErrorLog.Write($"Picker probe: HWND=0x{AppWindowInterop.WindowHandle.ToInt64():X}");
            _ = _services.GetRequiredService<IFilesService>().PickFileAsync();
            StartupErrorLog.Write("Picker probe: PickSingleFileAsync started OK");
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Picker probe FAILED: " + ex);
        }
    }

    /// <summary>
    /// Plays files supplied on the command line. An unpackaged app has no
    /// FileActivatedEventArgs, so <c>Screenbox.exe &lt;path&gt;</c> replaces both the
    /// Explorer file association and the old UWP file activation path.
    /// <para>
    /// The dispatch is delayed until the XAML tree (in particular PlayerElement and
    /// its view model) has finished loading; otherwise the SetQueueMessage would be
    /// sent before anything subscribes to it.
    /// </para>
    /// </summary>
    private async Task OpenCommandLineFilesAsync()
    {
        await Task.Delay(2000);

        try
        {
            List<IStorageItem> items = [];
            foreach (string rawPath in LaunchArguments.Files)
            {
                // Windows.Storage rejects forward slashes outright, and a path typed on the
                // command line very often has them.
                string path = FilesHelpers.NormalizePath(rawPath);
                try
                {
                    items.Add(await StorageFile.GetFileFromPathAsync(path));
                }
                catch (Exception ex)
                {
                    StartupErrorLog.Write("Cannot open command line file '" + path + "': " + ex.Message);
                }
            }

            if (items.Count == 0) return;

            StartupErrorLog.Write($"Command line playback: {items.Count} file(s)");
            var queueService = _services.GetRequiredService<IQueueService>();
            var settings = _services.GetRequiredService<GeneralSettingsContext>();
            await WeakReferenceMessenger.Default.PlayFilesAsync(
                items, queueService, settings.EnqueueAllFilesInFolder);
            StartupErrorLog.Write("Command line playback dispatched");
        }
        catch (Exception ex)
        {
            StartupErrorLog.Write("Command line playback failed: " + ex);
        }
    }

    private async Task StartupInitAsync()
    {
        var dbService = _services.GetRequiredService<IDatabaseService>();
        try
        {
            await dbService.InitializeAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Database initialization failed at startup.");
        }
    }

    [DynamicWindowsRuntimeCast(typeof(Frame))]
    private Frame InitRootFrame()
    {
        // Do not repeat app initialization when the Window already has content,
        // just ensure that the window is active
        if (WindowAccessor.Current.Content is not Frame rootFrame)
        {
            // Create a Frame to act as the navigation context and navigate to the first page
            rootFrame = new Frame();

            rootFrame.NavigationFailed += OnNavigationFailed;

            // Place the frame in the current Window
            WindowAccessor.Current.Content = rootFrame;
            SetMinWindowSize();

            // Check for RTL flow direction
            if (GlobalizationHelper.IsRightToLeftLanguage)
            {
                rootFrame.FlowDirection = FlowDirection.RightToLeft;
            }

            var settings = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<GeneralSettingsContext>();
            rootFrame.RequestedTheme = settings.Theme;
        }

        return rootFrame;
    }
}
