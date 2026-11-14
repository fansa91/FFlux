using System;
using Windows.System.Display;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Keeps the display awake during playback.
/// <para>
/// <c>Windows.System.Display.DisplayRequest</c> requires a UWP CoreWindow and throws
/// in an unpackaged WinUI 3 process. Construction is therefore best effort: when the
/// request cannot be created, all calls become no-ops instead of breaking startup.
/// </para>
/// </summary>
internal class DisplayRequestTracker
{
    public bool IsActive => _requestCount > 0;

    private readonly DisplayRequest? _displayRequest;
    private int _requestCount;

    public DisplayRequestTracker()
    {
        try
        {
            _displayRequest = new DisplayRequest();
        }
        catch (Exception)
        {
            _displayRequest = null;
        }
    }

    public void RequestActive()
    {
        if (_displayRequest is null) return;

        lock (_displayRequest)
        {
            try
            {
                _displayRequest.RequestActive();
                _requestCount++;
            }
            catch (Exception)
            {
                // Display requests are best effort on desktop.
            }
        }
    }

    public void RequestRelease()
    {
        if (_displayRequest is null) return;

        lock (_displayRequest)
        {
            if (_requestCount <= 0) return;
            try
            {
                _displayRequest.RequestRelease();
                _requestCount--;
            }
            catch (Exception)
            {
                // Display requests are best effort on desktop.
            }
        }
    }
}
