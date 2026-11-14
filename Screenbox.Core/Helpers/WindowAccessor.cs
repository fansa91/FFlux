using System;
using Microsoft.UI.Xaml;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Holds the single top-level window of the unpackaged WinUI 3 application.
/// <para>
/// UWP's <c>Window.Current</c> relies on a CoreApplication view backed by package
/// identity. In an unpackaged desktop app it either returns <see langword="null"/> or
/// throws 0x80073D54, so App.OnLaunched registers the window it creates here and
/// every consumer goes through <see cref="Current"/> instead.
/// </para>
/// </summary>
public static class WindowAccessor
{
    private static Window? _window;

    /// <summary>
    /// Raised when the main window is registered. Services constructed before the
    /// window exists can subscribe here and attach their event handlers later.
    /// </summary>
    public static event Action<Window>? WindowRegistered;

    /// <summary>Gets the main window, or <see langword="null"/> before it is created.</summary>
    public static Window? CurrentOrNull => _window;

    /// <summary>Gets the main window; throws a descriptive error if it does not exist yet.</summary>
    public static Window Current => _window
        ?? throw new InvalidOperationException("The application window has not been created yet.");

    /// <summary>Registers the window created by the application.</summary>
    public static void Register(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        WindowRegistered?.Invoke(window);
    }
}
