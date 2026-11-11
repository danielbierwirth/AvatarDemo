using System;
using SentisModels;
using VoiceAgent.UI;
using UnityEngine;

namespace VoiceAgent.Scenes
{
    /// <summary>
    /// The "Ask Nova anything" voice dialog on the NovaAvatarScene avatar, ported from mini-agent-unity's
    /// VoiceAgentDemoScene without function calling: mic → Silero VAD → SenseVoice (English, Korean) or Whisper (Arabic)
    /// → Gemma3 reply in the user's language → Supertonic 3 speech, which the avatar lip-syncs to (AvatarLipSync).
    /// The EN / KR / AR toggle picks the speech-to-text language; typed text works too. Everything runs on-device.
    /// </summary>
    public sealed class NovaAvatarDemo : MonoBehaviour
    {
        [SerializeField] AssistantView m_View;
        [SerializeField] SpeechOutput m_Speech;
        [Tooltip("Voice style picker (optional). Picking a style overrides the persona's per-language voices for the session.")]
        [SerializeField] VoiceSelector m_VoiceSelector;
        [Tooltip("Swaps the avatar to match the voice style: female (F*) and male (M*) voices each get their own model.")]
        [SerializeField] AvatarSwitcher m_Avatars;
        [Tooltip("Upper bound on generated tokens per reply (Korean and Arabic use more tokens per sentence than English).")]
        [SerializeField] int m_MaxReplyTokens = 64;

        ConversationController m_Controller;
        VoiceInput m_Voice;
        Gemma3Model m_Gemma;
        Persona m_Persona;

        void Awake()
        {
            m_Persona = Persona.Load();
            // In Awake, so only the chosen avatar's lip sync runs its Start (which loads the NeuroSync models).
            ShowAvatarFor(m_Persona.Voice(AppLanguage.Current));
            AppLanguage.Changed += OnLanguageChanged;
        }

        async void Start()
        {
            m_View.SetMicState(MicState.Off);
            m_View.SetInteractable(false);
            m_View.SetStatus("Loading models…");
            await Awaitable.NextFrameAsync();
            await Awaitable.NextFrameAsync();
            if (!this) return;
            await ModelRoots.PrepareAsync(m_View.SetStatus);
            if (!this) return;

            var clock = System.Diagnostics.Stopwatch.StartNew();
            m_Speech.Voice = m_Persona.voice;
            m_Speech.VoiceFor = language => m_Persona.Voice(language switch
            {
                SupertonicLanguage.ko => Lang.Ko,
                SupertonicLanguage.ar => Lang.Ar,
                _ => Lang.En,
            });
            try
            {
                m_Speech.Load(ModelRoots.Tts);
            }
            catch (Exception e)
            {
                // Replies still show as text (ConversationController skips speech when TTS isn't loaded).
                Debug.LogException(e);
            }
            try
            {
                m_Gemma = ModelRoots.LoadGemma(m_Persona.SystemPrompt(AppLanguage.Current));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                m_View.SetStatus($"Couldn't load Gemma3: {e.Message}");
                return;
            }
            m_View.SetStatus("Warming up Gemma3…");
            await m_Gemma.GenerateAsync("hi", 2);
            if (!this) return;
            m_Voice = new VoiceInput();

            m_Controller = new ConversationController(m_Persona, m_Gemma, m_View, m_Speech, m_MaxReplyTokens);
            m_View.MicClicked += ToggleListen;
            m_Voice.SpeechStarted += () => m_View.SetInputText("…");
            m_Voice.Partial += text => m_View.SetInputText(text);
            m_Voice.ListeningStopped += () =>
            {
                m_View.SetStatus(string.Empty);
                if (!m_Controller.IsBusy) m_View.SetMicState(MicState.Idle);
            };
            m_Voice.Final += async text =>
            {
                m_View.SetInputText(string.Empty);
                if (!string.IsNullOrWhiteSpace(text)) await m_Controller.Submit(text);
            };

            SetUpVoiceSelector();

            m_View.SetInteractable(true);
            m_View.SetMicState(MicState.Idle);
            m_View.SetStatus(string.Empty);
            Debug.Log($"[Nova] ready in {clock.Elapsed.TotalSeconds:F1} s");
            var greeting = m_Persona.Greeting(AppLanguage.Current);
            m_View.AddMessage(greeting, false);
        }

        void SetUpVoiceSelector()
        {
            if (m_VoiceSelector == null || !m_Speech.IsLoaded) return;
            m_VoiceSelector.Show(SpeechOutput.AvailableVoices(ModelRoots.Tts), m_Persona.Voice(AppLanguage.Current));
            m_VoiceSelector.Selected += OnVoiceSelected;
        }

        void OnLanguageChanged(Lang lang)
        {
            // Until a style is picked, the persona's voice for the selected language sets the highlight and the avatar.
            if (!string.IsNullOrEmpty(m_Speech.VoiceOverride)) return;
            var voice = m_Persona.Voice(lang);
            if (m_VoiceSelector != null) m_VoiceSelector.Highlight(voice);
            ShowAvatarFor(voice);
        }

        void ShowAvatarFor(string voice)
        {
            if (m_Avatars != null) m_Avatars.Show(voice);
        }

        void OnVoiceSelected(string voice)
        {
            m_Speech.VoiceOverride = voice;
            // Before the preview below starts, so the new avatar lip-syncs it.
            ShowAvatarFor(voice);
            Debug.Log($"[Nova] voice style {voice}");
            // A short sample in the selected language, unless a turn or the mic is in progress.
            if (m_Controller == null || m_Controller.IsBusy || m_Voice.IsListening) return;
            _ = m_Controller.Speak(m_Persona.VoicePreview(AppLanguage.Current));
        }

        // ---- test hooks (play mode, e.g. via script eval): the same paths typed text and a spoken utterance take ----

        public bool IsReady => m_Controller != null;
        public bool IsBusy => m_Controller == null || m_Controller.IsBusy || m_Speech.IsSpeaking;

        /// <summary>Runs a turn as if <paramref name="text"/> were typed and submitted.</summary>
        public void SubmitText(string text) => _ = m_Controller.Submit(text);

        /// <summary>
        /// Speaks <paramref name="text"/> with Supertonic, resamples it to 16 kHz and transcribes it with the selected
        /// language's speech-to-text model, whose final transcript then runs a voice turn. Returns the transcript.
        /// </summary>
        public async Awaitable<string> SpeakAndListen(string text)
        {
            var (pcm, rate) = await m_Speech.SynthesizeClip(text);
            var audio = Resample(pcm, rate, MicrophoneStream.SampleRate);
            string heard = null;
            void Capture(string t) => heard = t;
            m_Voice.Final += Capture;
            try
            {
                await m_Voice.TranscribeClipAsync(audio);
            }
            finally
            {
                m_Voice.Final -= Capture;
            }
            return heard;
        }

        static float[] Resample(float[] pcm, int from, int to)
        {
            if (from == to || pcm.Length == 0) return pcm;
            // Box low-pass over one output period first, so the decimation doesn't fold the highs into the speech band.
            var width = Math.Max(1, (int)Math.Round((double)from / to));
            var smooth = new float[pcm.Length];
            double sum = 0;
            for (var i = 0; i < pcm.Length; i++)
            {
                sum += pcm[i];
                if (i >= width) sum -= pcm[i - width];
                smooth[i] = (float)(sum / Math.Min(i + 1, width));
            }
            pcm = smooth;
            var output = new float[(int)((long)pcm.Length * to / from)];
            for (var i = 0; i < output.Length; i++)
            {
                var at = (double)i * from / to;
                var j = (int)at;
                var t = (float)(at - j);
                output[i] = j + 1 < pcm.Length ? pcm[j] * (1 - t) + pcm[j + 1] * t : pcm[^1];
            }
            return output;
        }

        void ToggleListen()
        {
            if (m_Voice == null) return;
            if (m_Voice.IsListening)
            {
                m_Voice.Stop();
                return;
            }
            // A turn submitted now would be dropped (one turn at a time).
            if (m_Controller.IsBusy)
            {
                m_View.SetStatus(AssistantView.BusyNotice);
                return;
            }
            m_Speech.Stop();
            if (!m_Voice.Listen())
            {
                m_View.SetStatus(AppLanguage.Current == Lang.Ar
                    ? "Can't listen: no microphone, or Whisper isn't installed (VoiceAgent ▸ Speech ▸ Download Whisper Base)."
                    : "Can't listen: no microphone, or the speech model didn't load.");
                return;
            }
            m_View.SetMicState(MicState.Listening);
            m_View.SetStatus("Listening…");
        }

        void Update()
        {
            if (m_Voice == null) return;
            m_Voice.Tick();
            if (m_Voice.IsListening) m_View.SetMicState(MicState.Listening, m_Voice.Level);
        }

        void OnDestroy()
        {
            AppLanguage.Changed -= OnLanguageChanged;
            m_Voice?.Dispose();
            m_Gemma?.Dispose();
        }
    }
}
