using System;
using UnityEngine;

namespace VoiceAgent
{
    /// <summary>
    /// Who the avatar is (Resources/Persona.json): the system prompt that shapes every Gemma3 reply, the line that pins
    /// the reply language, the greeting and the fallback line, each with an English, Korean (_ko) and Arabic (_ar)
    /// version, and the Supertonic voice style used per language (<see cref="Voice"/>).
    /// </summary>
    [Serializable]
    public sealed class Persona
    {
        public string name, system_prompt;
        public string voice, voice_ko, voice_ar;
        public string greeting, greeting_ko, greeting_ar;
        public string reply_language, reply_language_ko, reply_language_ar;
        public string not_sure, not_sure_ko, not_sure_ar;
        public string voice_preview, voice_preview_ko, voice_preview_ar;

        /// <summary>
        /// The system prompt for a turn in <paramref name="lang"/>: the persona plus one line naming only that language.
        /// (In the source project a rule naming several languages made Gemma answer English in Korean.)
        /// </summary>
        public string SystemPrompt(Lang lang) => $"{system_prompt} {Pick(lang, reply_language, reply_language_ko, reply_language_ar)}";

        /// <summary>Only the line pinning the reply language (the controller's retry prompt).</summary>
        public string ReplyLanguage(Lang lang) => Pick(lang, reply_language, reply_language_ko, reply_language_ar);

        public string Greeting(Lang lang) => Pick(lang, greeting, greeting_ko, greeting_ar);

        /// <summary>The sample line spoken when a voice style is picked in the UI.</summary>
        public string VoicePreview(Lang lang) => Pick(lang, voice_preview, voice_preview_ko, voice_preview_ar);

        public string NotSure(Lang lang) => Pick(lang, not_sure, not_sure_ko, not_sure_ar);

        /// <summary>The Supertonic voice style (F1–F5, M1–M5) for <paramref name="lang"/>, or <see cref="voice"/> when unset.</summary>
        public string Voice(Lang lang) => Pick(lang, voice, voice_ko, voice_ar);

        /// <summary>The value for <paramref name="lang"/>, or the English one when that one is missing.</summary>
        static string Pick(Lang lang, string en, string ko, string ar)
        {
            var line = AppLanguage.Pick(lang, en, ko, ar);
            return string.IsNullOrEmpty(line) ? en : line;
        }

        public static Persona Load() => JsonUtility.FromJson<Persona>(Resources.Load<TextAsset>("Persona").text);
    }
}
