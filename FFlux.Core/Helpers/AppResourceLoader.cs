using System;
using Microsoft.Windows.ApplicationModel.Resources;

namespace Screenbox.Core.Helpers;

/// <summary>
/// MRT Core backed string lookup for unpackaged WinUI 3 apps.
/// <para>
/// <c>Windows.ApplicationModel.Resources.ResourceLoader</c> resolves resources from
/// the app package and throws when the process has no package identity. MRT Core
/// (part of the Windows App SDK) reads the loose <c>resources.pri</c> next to the exe
/// instead, which is what an unpackaged build ships.
/// </para>
/// </summary>
public static class AppResourceLoader
{
    private static readonly Lazy<ResourceLoader?> Loader = new(static () =>
    {
        try
        {
            return new ResourceLoader();
        }
        catch (Exception)
        {
            return null;
        }
    });

    /// <summary>
    /// Looks up a resource by key and returns an empty string when MRT Core is not
    /// initialized or the key is missing. Never throws: a missing caption must not
    /// prevent the main window from appearing.
    /// </summary>
    public static string GetString(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        try
        {
            return Loader.Value?.GetString(key) ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
