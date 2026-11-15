using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Screenbox.Helpers;

/// <summary>
/// Captures media files passed on the command line.
/// <para>
/// An unpackaged WinUI 3 app has no file-type activation / AppInstance redirection,
/// so <c>Screenbox.exe "C:\clip.mp4"</c> is the replacement entry point (and the
/// one Explorer "Open with" will use once the association is registered).
/// </para>
/// </summary>
public static class LaunchArguments
{
    /// <summary>Existing local files supplied on the command line, in order.</summary>
    public static IReadOnlyList<string> Files { get; private set; } = Array.Empty<string>();

    public static void Capture(string[] args)
    {
        Files = (args ?? Array.Empty<string>())
            .Where(static arg => !string.IsNullOrWhiteSpace(arg) &&
                                 !arg.StartsWith('-') &&
                                 !arg.StartsWith('/'))
            .Select(static arg => arg.Trim('"'))
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
