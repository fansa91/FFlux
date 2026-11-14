using System;
using System.Runtime.InteropServices;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Windows.Graphics.Display.DisplayInformation.GetForCurrentView() requires a UWP
/// CoreWindow and fails with 0x80070490 in an unpackaged WinUI 3 process. The
/// system DPI is a good enough substitute for the crop-geometry calculation.
/// </summary>
public static class AppDisplayScale
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    public static double RawPixelsPerViewPixel
    {
        get
        {
            try
            {
                uint dpi = GetDpiForSystem();
                return dpi == 0 ? 1.0 : dpi / 96.0;
            }
            catch (Exception)
            {
                return 1.0;
            }
        }
    }
}
