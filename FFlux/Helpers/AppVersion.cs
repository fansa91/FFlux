using System;
using System.Reflection;

namespace Screenbox.Helpers;

/// <summary>
/// The product version as the user should see it, shared by the settings page caption and the
/// startup log line.
/// </summary>
/// <remarks>
/// The release pipeline stamps the git tag into <c>AssemblyInformationalVersion</c> with
/// <c>-p:Version=</c>, and that attribute is the only place the real product version lives:
/// <c>AssemblyVersion</c> is padded to four numeric parts for the loader, so a 0.1.0 build would
/// read as "0.1.0.0". The SDK appends the source revision as "+&lt;sha&gt;" metadata, which is
/// build provenance rather than part of the version, so it is trimmed off.
/// </remarks>
internal static class AppVersion
{
    /// <summary>
    /// Something like "0.1.0", or an empty string if the metadata cannot be read.
    /// </summary>
    internal static string Product { get; } = ReadProductVersion();

    private static string ReadProductVersion()
    {
        try
        {
            Assembly assembly = typeof(AppVersion).Assembly;

            string? informational = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrEmpty(informational))
            {
                int metadata = informational.IndexOf('+');
                return metadata > 0 ? informational[..metadata] : informational;
            }

            Version? version = assembly.GetName().Version;
            return version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
