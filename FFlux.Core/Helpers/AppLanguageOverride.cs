using System;
using System.IO;

namespace Screenbox.Core.Helpers;

/// <summary>
/// Stores the user's language override without going through
/// <c>Windows.Globalization.ApplicationLanguages</c>, which needs package identity and
/// throws 0x80073D54 in an unpackaged WinUI 3 process.
/// </summary>
public static class AppLanguageOverride
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDataMigration.CurrentFolderName, "language.txt");

    /// <summary>
    /// Language tags shipped in <c>Strings/*.resw</c>. An unpackaged app has no
    /// <c>ApplicationLanguages.ManifestLanguages</c> to enumerate, so the settings page
    /// uses this list instead.
    /// </summary>
    public static readonly string[] ShippedLanguages =
    [
        "ar-SA", "be-BY", "de-DE", "el-GR", "en-US", "es-ES", "fi-FI", "fr-FR", "he-IL",
        "hu-HU", "id-ID", "it-IT", "ja-JP", "ko-KR", "ms-MY", "nl-NL", "pl-PL", "pt-BR",
        "pt-PT", "ro-RO", "ru-RU", "si-LK", "tr-TR", "uk-UA", "vi", "zh-Hans", "zh-Hant"
    ];

    /// <summary>
    /// Gets or sets the BCP-47 language override. An empty value means "use the
    /// system default". Reads and writes never throw so startup cannot be blocked by
    /// a missing or locked user profile directory.
    /// </summary>
    public static string Current
    {
        get
        {
            try
            {
                return File.Exists(FilePath) ? File.ReadAllText(FilePath).Trim() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
        set
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, value ?? string.Empty);
            }
            catch (Exception)
            {
                // Language preference is best-effort only.
            }
        }
    }
}
