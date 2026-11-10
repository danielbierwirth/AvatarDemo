using System;
using UnityEngine;

namespace VoiceAgent
{
    /// <summary>The three supported languages (the order matches the source project).</summary>
    public enum Lang { Ko, En, Ar }

    /// <summary>
    /// The one language setting the scene uses (EN / KR / AR toggle, see <see cref="LanguageToggle"/>). It picks the
    /// speech-to-text language, the language Nova replies in, and the TTS voice language. Every run starts in
    /// <see cref="Default"/> (English); <see cref="Changed"/> fires on every switch so listeners update mid-session
    /// without a reload.
    /// </summary>
    public static class AppLanguage
    {
        public const Lang Default = Lang.En;

        static Lang s_Current = Default;

        public static event Action<Lang> Changed;

        public static Lang Current => s_Current;

        public static void Set(Lang lang)
        {
            if (s_Current == lang) return;
            s_Current = lang;
            Debug.Log($"[Language] {Code(lang)}");
            Changed?.Invoke(lang);
        }

        /// <summary>ISO 639-1 code: en, ko, ar.</summary>
        public static string Code(Lang lang) => lang switch { Lang.Ko => "ko", Lang.Ar => "ar", _ => "en" };

        public static Lang Parse(string code) => (code ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "ko" or "kr" => Lang.Ko,
            "ar" => Lang.Ar,
            _ => Lang.En,
        };

        /// <summary>One of three strings by language.</summary>
        public static string Pick(Lang lang, string en, string ko, string ar) => lang switch { Lang.Ko => ko, Lang.Ar => ar, _ => en };

        public static string Pick(string en, string ko, string ar) => Pick(Current, en, ko, ar);

        // Enter Play Mode without a domain reload keeps statics: start each session in English with no stale listeners.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSession()
        {
            Changed = null;
            s_Current = Default;
        }
    }
}
