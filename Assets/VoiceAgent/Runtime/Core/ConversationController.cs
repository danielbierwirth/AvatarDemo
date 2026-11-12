using System.Linq;
using SentisModels;
using VoiceAgent.Info;
using VoiceAgent.UI;
using UnityEngine;

namespace VoiceAgent
{
    /// <summary>
    /// Turn logic of the conversational avatar, ported from mini-agent-unity's AssistantController without its
    /// smart-home commands: every typed or spoken turn goes to the language model (qwen via Ollama, <see cref="LlmClient"/>)
    /// with the persona's system prompt, or to Gemma3 on-device when it can't be reached (no Ollama, a phone) or answers
    /// outside the reply language. The reply is shown in the chat log and spoken by Supertonic (which also drives the
    /// avatar's lip-sync).
    ///
    /// Questions about the world are answered from the internet with mini-agent's <see cref="InfoAgent"/>: weather and
    /// the time in a city (Open-Meteo), where the user is (IP lookup), and web search (Wikidata, Wikipedia, news), with
    /// qwen writing the answer from what the search found. mini-agent picks these with its Decision AI head; here qwen
    /// picks them with tool calling (<see cref="s_Tools"/>) in a short routing request before the reply
    /// (<see cref="RouterPrompt"/>); world facts qwen already knows it usually answers itself. Arabic questions the
    /// <see cref="ArabicCommands"/> rules recognise go to web search directly, as in mini-agent. Without qwen, only those
    /// do.
    ///
    /// Reply language: the language of what the user said or typed. Hangul text gets a Korean reply, Arabic text an
    /// Arabic one, other text an English one. Text without letters uses the EN / KR / AR toggle. The system prompt names
    /// only that language. Gemma3-270M doesn't always keep to Korean or Arabic, so a reply in the wrong script (or one
    /// that only repeats the question) is replaced with the persona's "not sure" line in the right language instead of
    /// being shown and spoken.
    /// </summary>
    public sealed class ConversationController
    {
        readonly Persona m_Persona;
        readonly Gemma3Model m_Gemma;
        readonly LlmClient m_Llm;
        readonly InfoAgent m_Info;
        readonly AssistantView m_View;
        readonly SpeechOutput m_Speech;
        readonly int m_MaxReplyTokens;
        bool m_Busy;
        int m_Turn;

        /// <summary>
        /// The avatar can't act from this conversation, so it must not claim it did something, and it stays the persona
        /// instead of naming the model behind it.
        /// </summary>
        const string ChatRules = "Only if the user asks you to do something (switch a device, book, send), say you can't do that here; never claim you did it. Never say which AI model you are or who made it.";

        /// <summary>The internet lookups qwen can call instead of answering (OpenAI tool calling); each maps to an <see cref="InfoAgent"/> command.</summary>
        static readonly LlmClient.Tool[] s_Tools =
        {
            new(InfoAgent.WebSearch, "Look up a factual question about the world on the web: people, places, things, history, science, news, sports, prices, dates or numbers."),
            new("get_weather", "Get the weather forecast, for a named city or where the user is."),
            new("get_time", "Get the current time, in a named city or where the user is."),
            new("get_location", "Find where the user is."),
        };

        /// <summary>
        /// The routing request's prompt. It is short on purpose: under the full persona prompt and the strict reply-language
        /// line, qwen3.5 answered weather questions itself (made up a forecast) instead of calling the tool, most often in
        /// Korean and Arabic, and it answers world facts from memory (wrongly, at times) unless told that its knowledge may
        /// be out of date. This prompt picked the right tool, or none, for 20 of 20 EN/KO/AR questions in two runs.
        /// </summary>
        const string RouterPrompt =
            "You are Nova, a friendly avatar. Keep replies to one or two short sentences. Your own knowledge may be wrong or out of date: " +
            "for any question that asks for a fact about the world (who, what, where, when, how many, news, sports, people, places, history, " +
            "science, prices or numbers), call web_search even if you think you know the answer. Call the weather, time or location tool for " +
            "those questions. Answer small talk, feelings, jokes, opinions, recommendations and questions about yourself directly. " +
            "Reply ONLY in the language the user writes in.";

        /// <summary>
        /// Enough tokens for a tool call (qwen3.5 adds a query argument to web_search; at 24 tokens that call was cut off and
        /// came back as no tool); a reply that starts answering instead is cut off here and thrown away.
        /// </summary>
        const int RouterTokens = 64;

        enum Tool { None, Search, Weather, Time, Location }

        public bool IsBusy => m_Busy;

        public ConversationController(Persona persona, Gemma3Model gemma, AssistantView view, SpeechOutput speech, int maxReplyTokens = 64,
            LlmClient llm = null, InfoAgent info = null)
        {
            m_Llm = llm;
            m_Info = info;
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
            var turn = ++m_Turn;
            m_View.SetMicState(MicState.Thinking);
            var bubble = m_View.AddMessage("…", false);
            string answer = null, shown = null, writer = "Gemma3";
            try
            {
                // The Arabic rules recognise questions (dialect forms too) and go straight to the search, as in mini-agent.
                var tool = m_Info != null && ArabicSearch(text) ? Tool.Search : Tool.None;

                // First a short routing request: does the turn need an internet lookup (qwen tool calling)?
                if (m_Llm != null && m_Info != null && tool == Tool.None)
                {
                    var (_, called) = await m_Llm.ChatAsync(RouterPrompt, text, s_Tools, maxTokens: RouterTokens);
                    tool = ToolOf(called);
                    if (called != null) Debug.Log($"[Nova] {m_Llm.ShortName} called {called}");
                }
                // Otherwise the language model answers in the persona's voice (it knows the world and keeps to the
                // language); Gemma3 on-device when it can't be reached or its reply isn't usable.
                if (m_Llm != null && tool == Tool.None && turn == m_Turn)
                {
                    var system = $"{m_Persona.SystemPrompt(lang)} {ChatRules} {AnswerWriter.LanguageRule(lang)} {AnswerWriter.SpokenStyle}";
                    answer = Usable(AnswerWriter.Clean(await m_Llm.CompleteAsync(system, text), lang), lang, text, m_Llm.ShortName);
                    if (answer != null) writer = m_Llm.ShortName;
                    else Debug.Log($"[Nova] {m_Llm.ShortName} gave no usable reply ({m_Llm.LastError ?? "not in the reply language"}), Gemma3 answers");
                }
                if (tool != Tool.None && turn == m_Turn)
                {
                    (answer, shown, writer) = await LookUp(tool, text, lang, bubble);
                    if (answer == null) writer = "Gemma3";
                }
                if (answer == null && turn == m_Turn)
                {
                    answer = Usable(await Generate(text, m_Persona.SystemPrompt(lang), bubble), lang, text);
                    // Gemma3-270M sometimes ends its turn at once (an empty reply) or drifts out of the language under the
                    // full persona prompt; one retry with only the reply-language rule usually gets a real answer.
                    if (answer == null && turn == m_Turn) answer = Usable(await Generate(text, m_Persona.ReplyLanguage(lang), bubble), lang, text);
                }
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

            // The language was switched mid-turn (Reset): the chat was cleared, so this reply is neither shown nor spoken.
            if (turn != m_Turn)
            {
                m_View.SetMicState(MicState.Idle);
                return;
            }
            if (string.IsNullOrWhiteSpace(answer)) answer = shown = null;
            answer ??= m_Persona.NotSure(lang);
            Debug.Log($"[Nova] ({AppLanguage.Code(lang)}, {writer}) '{text}' → '{answer}'");
            m_View.UpdateMessage(bubble, shown ?? answer);
            await Speak(answer);
        }

        /// <summary>True when the Arabic rules read <paramref name="text"/> as a question for the web (mini-agent's routing).</summary>
        static bool ArabicSearch(string text) =>
            ArabicText.ContainsArabic(text) && ArabicCommands.ToEnglish(text) is { } rewrite &&
            rewrite.StartsWith(ArabicCommands.SearchPrefix, System.StringComparison.Ordinal);

        static Tool ToolOf(string called) => called switch
        {
            InfoAgent.WebSearch => Tool.Search,
            "get_weather" => Tool.Weather,
            "get_time" => Tool.Time,
            "get_location" => Tool.Location,
            _ => Tool.None,
        };

        /// <summary>
        /// Answers from the internet: (spoken answer, bubble text with the source line, who wrote it), or a null answer
        /// when the search found nothing and no model could write one, so Gemma3 answers instead.
        /// </summary>
        async Awaitable<(string Speech, string Shown, string Writer)> LookUp(Tool tool, string text, Lang lang, MessageView bubble)
        {
            var command = tool switch
            {
                Tool.Weather => "get_weather",
                Tool.Time => "get_time",
                Tool.Location => "get_location",
                _ => InfoAgent.WebSearch,
            };
            // The place lookups read English and Korean; Arabic weather/time questions go in as their English rewrite.
            var question = command == InfoAgent.WebSearch ? text : ArabicCommands.ForModel(text);
            m_View.UpdateMessage(bubble, InfoAgent.LookingUp(lang));
            var info = await m_Info.AnswerAsync(command, question, lang, step => m_View.UpdateMessage(bubble, step));
            if (info == null)
            {
                // get_time without a city: the local clock.
                var now = ClockText.Format(System.DateTime.Now, lang);
                var local = AppLanguage.Pick(lang, $"It's {now}.", $"지금 {now}이에요.", $"الساعة الآن {now}.");
                return (local, null, "local clock");
            }
            foreach (var step in info.Steps) Debug.Log($"[Info] {step}");
            if (!info.Found && command == InfoAgent.WebSearch) return (null, null, null);
            var speech = info.Speech;
            return (speech, WithSources(speech, info.Sources, lang), InfoAgent.ActionLabel(command, info).Replace("Action / ", string.Empty));
        }

        static string WithSources(string text, string sources, Lang lang) =>
            string.IsNullOrEmpty(sources) ? text : $"{text}\n<size=15><color=#6B6780>{AppLanguage.Pick(lang, "Source", "출처", "المصدر")} / {sources}</color></size>";

        /// <summary>
        /// Starts fresh (on a language switch): stops the reply being spoken and drops the answer of a turn still being
        /// generated, so it isn't shown or spoken in the new chat.
        /// </summary>
        public void Reset()
        {
            m_Turn++;
            m_Speech?.Stop();
        }

        async Awaitable<string> Generate(string text, string systemPrompt, MessageView bubble)
        {
            m_Gemma.SystemPrompt = systemPrompt;
            return ReplyText.Clean(await m_Gemma.GenerateAsync(text, m_MaxReplyTokens, partial => m_View.UpdateMessage(bubble, ReplyText.Clean(partial))));
        }

        /// <summary>The reply, or null when it is empty, not in <paramref name="lang"/> or only repeats <paramref name="question"/>.</summary>
        static string Usable(string reply, Lang lang, string question, string writer = "Gemma3")
        {
            if (string.IsNullOrWhiteSpace(reply)) return null;
            if (!AnswerWriter.InLanguage(reply, lang))
            {
                Debug.Log($"[Nova] {writer} answered outside {AppLanguage.Code(lang)}, discarded: {reply}");
                return null;
            }
            // Gemma3-270M sometimes just repeats the question ("أين يقع لبنان؟" → "أين يقع لبنان؟"): that is no answer.
            if (Comparable(reply) == Comparable(question))
            {
                Debug.Log($"[Nova] {writer} repeated the question, discarded: {reply}");
                return null;
            }
            return reply;
        }

        /// <summary>Text reduced to its letters and digits, lower-cased, with Arabic letter variants folded together.</summary>
        static string Comparable(string text) =>
            new string(ArabicText.Normalize(text).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

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
