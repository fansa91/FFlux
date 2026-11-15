using System;
using Microsoft.UI;
using WinRT.Interop;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Bridges WinRT APIs that need an owner window (HWND) to the single unpackaged
/// WinUI 3 window. UWP supplied implicit owner views; a desktop app has to hand
/// the handle to <c>IInitializeWithWindow</c> explicitly or the picker/launcher
/// fails with 0x80070578 ("Invalid window handle").
/// </summary>
public static class AppWindowInterop
{
    /// <summary>Gets the native handle of the main window, or <see cref="IntPtr.Zero"/>.</summary>
    public static IntPtr WindowHandle
    {
        get
        {
            var window = WindowAccessor.CurrentOrNull;
            if (window is null) return IntPtr.Zero;

            try
            {
                return WindowNative.GetWindowHandle(window);
            }
            catch (Exception)
            {
                // Some hosting arrangements expose the AppWindow before the XAML
                // window has a native HWND; the window id can be translated instead.
                try
                {
                    return Win32Interop.GetWindowFromWindowId(window.AppWindow.Id);
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }
        }
    }

    /// <summary>
    /// Binds a WinRT object (picker, LauncherOptions, etc.) to the main window and
    /// returns it so it can be used inline. Objects that do not implement
    /// <c>IInitializeWithWindow</c> are returned untouched — those APIs work without
    /// an owner handle.
    /// </summary>
    public static T Initialize<T>(T target) where T : class
    {
        if (target is null) return target;

        IntPtr hwnd = WindowHandle;
        if (hwnd == IntPtr.Zero) return target;

        try
        {
            InitializeWithWindow.Initialize(target, hwnd);
        }
        catch (Exception)
        {
            // Best effort: never let an interop detail break a user action.
        }

        return target;
    }
}
