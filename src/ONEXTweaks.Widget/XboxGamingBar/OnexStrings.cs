// SPDX-License-Identifier: AGPL-3.0-only
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Storage;

namespace XboxGamingBar
{
    // UI text only. Stored action IDs, commands and user-provided names never pass
    // through a general tree translator or a culture-dependent serialization path.
    internal static class OnexStrings
    {
        internal const string Setting = "OnexUiLanguage";
        internal static string Language { get; private set; } = "en";
        internal static string Normalize(string language) => language == "ru" ? "ru" : "en";
        internal static void Initialize()
        {
            try { var values=ApplicationData.Current.LocalSettings.Values; Language=values.TryGetValue(Setting,out object selected)?Normalize(selected as string):"en"; }
            catch { Language = "en"; }
            Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride=Language=="ru"?"ru-RU":"en-US";
        }
        internal static string Get(string source) => GetForLanguage(source, Language);
        internal static string GetForLanguage(string source, string language)
        {
            if (source == null) return null;
            return OnexTranslationCatalog.Entries.TryGetValue(source, out var pair)
                ? pair[Normalize(language) == "ru" ? 1 : 0] : source;
        }
        internal static string Format(string source, params object[] arguments)
        {
            var culture = CultureInfo.GetCultureInfo(Language == "ru" ? "ru-RU" : "en-US");
            try { return string.Format(culture, Get(source), arguments); }
            catch (FormatException) { return string.Format(CultureInfo.InvariantCulture, source, arguments); }
        }
        // The component manager keeps canonical protocol values. Translate only
        // its known presentation messages at the widget boundary, never its JSON.
        internal static string ComponentMessage(string source)
        {
            string translated = Get(source);
            if (Language != "ru" || translated != source || string.IsNullOrEmpty(source)) return translated;
            const string preservation = " Current settings changed while the installer was open; the latest settings were kept and no old snapshot was restored.";
            if (source.EndsWith(preservation, StringComparison.Ordinal))
                return ComponentMessage(source.Substring(0, source.Length - preservation.Length)) + Get(preservation);
            if (source.StartsWith("Checking ", StringComparison.Ordinal) && source.EndsWith("…", StringComparison.Ordinal))
                return Format("Checking {0}…", source.Substring(9, source.Length - 10));
            const string download = "Downloading ", official = " from its official release…";
            if (source.StartsWith(download, StringComparison.Ordinal))
            {
                if (source.EndsWith(official, StringComparison.Ordinal))
                    return Format("Downloading {0} from its official release…", source.Substring(download.Length, source.Length - download.Length - official.Length));
                int separator = source.LastIndexOf(": ", StringComparison.Ordinal);
                if (separator > download.Length && source.EndsWith("%", StringComparison.Ordinal))
                    return Format("Downloading {0}: {1}%", source.Substring(download.Length, separator - download.Length), source.Substring(separator + 2, source.Length - separator - 3));
            }
            foreach (string prefix in new[] { "Installer returned error ", "GitHub returned HTTP ", "Installed state: " })
                if (source.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string value = source.Substring(prefix.Length);
                    bool dot = prefix != "Installed state: ";
                    return Format(prefix + "{0}" + (dot ? "." : ""), Get(dot ? value.TrimEnd('.') : value));
                }
            return source; // OS/vendor diagnostics are external data, not identifiers to rewrite.
        }
        internal static string ComponentServiceState(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            var parts = source.Split(new[] { " · " }, StringSplitOptions.None);
            for (int n = 0; n < parts.Length; n++)
            {
                if (parts[n].StartsWith("DSA: ", StringComparison.Ordinal))
                    parts[n] = "DSA: " + Get(parts[n].Substring(5));
                else if (parts[n].StartsWith("Updater: ", StringComparison.Ordinal))
                    parts[n] = Get("Updater: ") + Get(parts[n].Substring(9));
                else parts[n] = Get(parts[n]);
            }
            return string.Join(" · ", parts);
        }
    }
}
