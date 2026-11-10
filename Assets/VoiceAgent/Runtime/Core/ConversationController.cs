using SentisModels;
using VoiceAgent.UI;
using UnityEngine;

namespace VoiceAgent
{
    /// <summary>
    /// Turn logic of the conversational avatar, ported from mini-agent-unity's AssistantController without its command
    /// routing and tools: every typed or spoken turn goes straight to Gemma3 with the persona's system prompt, and the
    /// reply is shown in the chat log and spoken by Supertonic (which also drives the avatar's lip-sync).
    ///
    /// Reply language: the language of what the user said or typed. Hangul text gets a Korean reply, Arabic text an
    /// Arabic one, other text an English one. Text without letters uses the EN / KR / AR toggle. The system prompt names
    /// only that language. Gemma3-270M doesn't always keep to Korean or Arabic, so a reply in the wrong script is
    /// replaced with the persona's "not sure" line in the right language instead of being shown and spoken.
    /// </summary>
    public sealed class ConversationController
    {
        readonly Persona m_Persona;
        readonly Gemma3Model m_Gemma;
        readonly AssistantView m_View;
        readonly SpeechOutput m_Speech;
        readonly int m_MaxReplyTokens;
        bool m_Busy;

        public bool IsBusy => m_Busy;

        public ConversationController(Persona persona, Gemma3Model gemma, AssistantView view, SpeechOutput speech, int maxReplyTokens = 64)
        {
            m_Persona = persona;
            m_Gemma = gemma;
            m_View = view;
            m_Speech = speech;
            m_MaxReplyTokens = maxReplyTokens;

            view.CanSubmit = () => !m_Busy;
            // async void handler so exceptions reach the Unity log instead of vanishing with a discarded Awaitable.
            view.Submitted += async text => await Submit(text);
        }

        /// <summary>The language a reply to <paramref name="text"/> is in: the text's script, else the selected language.</summary>
        public static Lang ReplyLanguage(string text)
        {
            int hangul = 0, arabic = 0, letters = 0;
            foreach (var c in text ?? string.Empty)
            {
                if (!char.IsLetter(c)) continue;
                letters++;
                if (c >= '가' && c <= '힣') hangul++;
                else if (ArabicText.IsArabicLetter(c)) arabic++;
            }
            if (letters == 0) return AppLanguage.Current;
            if (arabic * 10 >= letters * 3) return Lang.Ar;
            if (hangul * 10 >= letters * 3) return Lang.Ko;
            return Lang.En;
        }

        public async Awaitable Submit(string text)
        {
            if (m_Busy || string.IsNullOrWhiteSpace(text)) return;
            m_View.AddMessage(text, true);
            var lang = ReplyLanguage(text);

            m_Busy = true;
            m_View.SetMicState(MicState.Thinking);
            var bubble = m_View.AddMessage("…", false);
            string answer = null;
            try
            {
                answer = Usable(await Generate(text, m_Persona.SystemPrompt(lang), bubble), lang);
                // Gemma3-270M sometimes ends its turn at once (an empty reply) or drifts out of the language under the
                // full persona prompt; one retry with only the reply-language rule usually gets a real answer.
                if (answer == null) answer = Usable(await Generate(text, m_Persona.ReplyLanguage(lang), bubble), lang);
            }
            catch (System.Exception e)
            {
                // The turn still ends with a reply, so the mic and the bubble don't stay on "thinking".
                Debug.LogException(e);
                answer = null;
            }
            finally
            {
                m_Busy = false;
            }

            if (string.IsNullOrWhiteSpace(answer)) answer = m_Persona.NotSure(lang);
            Debug.Log($"[Nova] ({AppLanguage.Code(lang)}) '{text}' → '{answer}'");
            m_View.UpdateMessage(bubble, answer);
            await Speak(answer);
        }

        async Awaitable<string> Generate(string text, string systemPrompt, MessageView bubble)
        {
            m_Gemma.SystemPrompt = systemPrompt;
            return ReplyText.Clean(await m_Gemma.GenerateAsync(text, m_MaxReplyTokens, partial => m_View.UpdateMessage(bubble, ReplyText.Clean(partial))));
        }

        /// <summary>The reply, or null when it is empty or not in <paramref name="lang"/>.</summary>
        static string Usable(string reply, Lang lang)
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;
            if (InLanguage(reply, lang)) return reply;
            Debug.Log($"[Nova] Gemma answered outside {AppLanguage.Code(lang)}, discarded: {reply}");
            return null;
        }

        /// <summary>Korean and Arabic replies must contain that script; English ones must contain neither.</summary>
        static bool InLanguage(string reply, Lang lang) => lang switch
        {
            Lang.Ar => ArabicText.ContainsArabic(reply),
            Lang.Ko => ContainsHangul(reply),
            _ => !ArabicText.ContainsArabic(reply) && !ContainsHangul(reply),
        };

        static bool ContainsHangul(string text)
        {
            foreach (var c in text)
                if (c >= '가' && c <= '힣') return true;
            return false;
        }

        public async Awaitable Speak(string text)
        {
            if (m_Speech == null || !m_Speech.IsLoaded)
            {
                m_View.SetMicState(MicState.Idle);
                return;
            }
            m_View.SetMicState(MicState.Speaking);
            await m_Speech.Speak(text);
            while (m_Speech.IsSpeaking) await Awaitable.NextFrameAsync();
            m_View.SetMicState(MicState.Idle);
        }
    }
}
