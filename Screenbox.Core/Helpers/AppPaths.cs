using System;
using System.IO;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Plain file-system paths for an unpackaged app. Replaces
/// <c>ApplicationData.Current.LocalFolder</c> / <c>TemporaryFolder</c> and
/// <c>Package.Current.InstalledLocation</c>, all of which require package identity.
/// </summary>
public static class AppPaths
{
    /// <summary>Per-user data folder, mirroring the storage used by SettingsStorage.</summary>
    public static string LocalFolder => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Screenbox"));

    /// <summary>Scratch folder for snapshots and transient library data.</summary>
    public static string TemporaryFolder => Ensure(Path.Combine(Path.GetTempPath(), "Screenbox"));

    /// <summary>Application install directory (the exe's folder).</summary>
    public static string InstalledLocation => AppContext.BaseDirectory;

    private static string Ensure(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception)
        {
            // Callers handle an unavailable folder through their own error paths.
        }

        return path;
    }
}
