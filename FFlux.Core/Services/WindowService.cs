using System;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Screenbox.Core.Contexts;
using Screenbox.Core.Enums;
using Screenbox.Core.Helpers;
using Windows.Foundation;
using Windows.Graphics;

namespace Screenbox.Core.Services;

/// <summary>
/// WinUI 3 implementation of <see cref="IWindowService"/>.
/// <para>
/// The UWP version used <c>ApplicationView.GetForCurrentView()</c>,
/// <c>CoreWindow</c> and <c>ViewModePreferences</c>. All three need a CoreApplication
/// view backed by package identity and throw in an unpackaged desktop app. This version
/// uses <see cref="AppWindow"/> from the Windows App SDK instead; operations the desktop
/// windowing API does not expose (compact overlay, cursor hiding) degrade gracefully.
/// </para>
/// </summary>
public sealed class WindowService : IWindowService
{
    public WindowViewMode ViewMode => _windowContext.ViewMode;

    private readonly WindowContext _windowContext;
    private Window? _window;

    public WindowService(WindowContext windowContext)
    {
        _windowContext = windowContext;

        // The service can be resolved before the window exists, so handle both orders.
        WindowAccessor.WindowRegistered += OnWindowRegistered;
        if (WindowAccessor.CurrentOrNull is { } existing) OnWindowRegistered(existing);
    }

    private void OnWindowRegistered(Window window)
    {
        if (_window is not null) _window.SizeChanged -= OnWindowSizeChanged;
        _window = window;
        window.SizeChanged += OnWindowSizeChanged;
    }

    private AppWindow? AppWindow
    {
        get
        {
            try
            {
                return _window?.AppWindow;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
    {
        if (_windowContext.ViewMode is not (WindowViewMode.FullScreen or WindowViewMode.Compact)) return;

        try
        {
            AppWindowPresenterKind expected = _windowContext.ViewMode == WindowViewMode.FullScreen
                ? AppWindowPresenterKind.FullScreen
                : AppWindowPresenterKind.CompactOverlay;
            if (AppWindow?.Presenter?.Kind != expected)
                _windowContext.ViewMode = WindowViewMode.Default;
        }
        catch (Exception)
        {
            _windowContext.ViewMode = WindowViewMode.Default;
        }
    }

    public bool TryEnterFullScreen()
    {
        try
        {
            AppWindow? appWindow = AppWindow;
            if (appWindow is null) return false;

            appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            _windowContext.ViewMode = WindowViewMode.FullScreen;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void ExitFullScreen()
    {
        try
        {
            AppWindow?.SetPresenter(AppWindowPresenterKind.Default);
        }
        catch (Exception)
        {
            // Best effort; the view mode is still reset below.
        }

        if (_windowContext.ViewMode == WindowViewMode.FullScreen)
            _windowContext.ViewMode = WindowViewMode.Default;
    }

    public Task<bool> TryExitCompactLayoutAsync()
    {
        // WinUI 3 desktop has no ApplicationViewMode.CompactOverlay. Leaving a compact
        // layout is therefore just restoring the default presenter.
        try
        {
            AppWindow?.SetPresenter(AppWindowPresenterKind.Default);
            if (_windowContext.ViewMode == WindowViewMode.Compact)
                _windowContext.ViewMode = WindowViewMode.Default;
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> TryEnterCompactLayoutAsync(Size viewSize)
    {
        // WinUI 3 desktop does have AppWindowPresenterKind.CompactOverlay (the old UWP
        // ApplicationViewMode.CompactOverlay was the unavailable API). SetPresenter throws when
        // the OS or window configuration cannot host it, which is our capability check.
        try
        {
            AppWindow? appWindow = AppWindow;
            if (appWindow is null) return Task.FromResult(false);

            if (viewSize.Width > 0 && viewSize.Height > 0)
                appWindow.Resize(new SizeInt32((int)Math.Round(viewSize.Width), (int)Math.Round(viewSize.Height)));

            appWindow.SetPresenter(AppWindowPresenterKind.CompactOverlay);
            _windowContext.ViewMode = WindowViewMode.Compact;
            StartupTrace.Write($"pip.enter (presenter={appWindow.Presenter?.Kind}, size={viewSize.Width}x{viewSize.Height})");
            return Task.FromResult(true);
        }
        catch (Exception e)
        {
            StartupTrace.Write("pip.enter.failed", e);
            return Task.FromResult(false);
        }
    }

    public Size GetMaxWindowSize()
    {
        try
        {
            DisplayArea? displayArea = DisplayArea.Primary;
            if (displayArea is not null)
            {
                RectInt32 workArea = displayArea.WorkArea;
                return new Size(Math.Max(320, workArea.Width - 16), Math.Max(240, workArea.Height - 16));
            }
        }
        catch (Exception)
        {
            // Fall through to the conservative fallback.
        }

        return new Size(1600, 900);
    }

    public double ResizeWindow(Size desiredSize, double scalar = 1)
    {
        if (scalar < 0 || desiredSize.IsEmpty || desiredSize.Width <= 0 || desiredSize.Height <= 0)
            return -1;

        Size maxWindowSize = GetMaxWindowSize();
        double maxWidth = maxWindowSize.Width;
        double maxHeight = maxWindowSize.Height;

        if (scalar == 0)
        {
            double widthRatio = maxWidth / desiredSize.Width;
            double heightRatio = maxHeight / desiredSize.Height;
            scalar = Math.Min(widthRatio, heightRatio);
        }

        double aspectRatio = desiredSize.Width / desiredSize.Height;
        double newWidth = desiredSize.Width * scalar;
        if (newWidth > maxWidth) newWidth = maxWidth;
        double newHeight = newWidth / aspectRatio;
        scalar = newWidth / desiredSize.Width;

        try
        {
            AppWindow? appWindow = AppWindow;
            if (appWindow is null) return -1;

            appWindow.Resize(new SizeInt32(
                (int)Math.Round(newWidth),
                (int)Math.Round(newHeight)));
            return scalar;
        }
        catch (Exception)
        {
            return -1;
        }
    }

    public void HideCursor()
    {
        // CoreWindow.PointerCursor is UWP-only. WinUI 3 requires an InputCursor attached
        // to a specific UIElement, which this service does not own.
    }

    public void ShowCursor()
    {
        // See HideCursor.
    }
}
