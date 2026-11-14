using System.Linq;
using Screenbox.Core.Helpers;

namespace Screenbox.Core.Helpers;

internal static class LanguageHelper
{
    public static bool TryConvertISO6392ToISO6391(string threeLetterTag, out string twoLetterTag)
    {
        twoLetterTag = string.Empty;
        if (threeLetterTag.Length != 3) return false;
        twoLetterTag = AppResourceLoader.GetString(threeLetterTag);
        return !string.IsNullOrEmpty(twoLetterTag);
    }

    public static string GetPreferredLanguage()
    {
        return Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? string.Empty;
    }
}
