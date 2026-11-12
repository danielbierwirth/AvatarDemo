using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VoiceAgent.UI;

namespace VoiceAgent.Scenes
{
    /// <summary>
    /// Demo mode for the NovaAvatarScene, modelled on mini-agent-unity's MiniAgentTestRunner: runs a scripted list of
    /// questions through <see cref="NovaAvatarDemo"/>, one at a time, in the selected language (English, Korean or Arabic
    /// list). Each question goes the same way typed text does, so the avatar answers and speaks every one; the next
    /// question waits until the reply has been spoken, plus <see cref="m_Interval"/> seconds.
    ///
    /// The <b>Demo</b> button (built at runtime on its own overlay canvas, styled like <see cref="LanguageToggle"/>) starts
    /// the list and stops it mid-run (the turn already running finishes; no further questions are submitted). Tick
    /// <see cref="m_AutoRun"/> to start it when the scene plays, after <see cref="m_StartDelay"/> seconds.
    /// </summary>
    public sealed class NovaDemoRunner : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds to wait after the scene starts (and Nova is ready) before the first question, with Auto Run.")]
        [Min(0f)]
        [SerializeField] float m_StartDelay = 3f;

        [Tooltip("Seconds to pause after Nova finished speaking before the next question.")]
        [Min(0f)]
        [SerializeField] float m_Interval = 2f;

        [Tooltip("Run the question list automatically when the scene starts.")]
        [SerializeField] bool m_AutoRun;

        [Tooltip("Start again from the top when the list is done (for an unattended booth demo).")]
        [SerializeField] bool m_Loop;

        [Header("Button placement (reference resolution 1920 x 1080)")]
        [Tooltip("Show the Demo start / stop button.")]
        [SerializeField] bool m_ShowButton = true;
        [Tooltip("Anchor and pivot: (0,1) top-left, (0.5,1) top-center, (1,1) top-right.")]
        [SerializeField] Vector2 m_Corner = new(0f, 1f);
        [Tooltip("Default: under the voice style picker (its top at -168, 26 + 2 x 40 + 4 tall), with the same 16 gap as above it.")]
        [SerializeField] Vector2 m_Offset = new(32f, -294f);
        [Tooltip("Default width matches the language toggle and voice style picker.")]
        [SerializeField] Vector2 m_Size = new(296f, 48f);
        [SerializeField] int m_FontSize = 22;
        [SerializeField] int m_SortingOrder = 1001;
        [SerializeField] string m_IdleLabel = "Demo";
        [SerializeField] string m_RunningLabel = "Stop";
        [SerializeField] Color m_Background = new(0.16f, 0.17f, 0.22f, 0.9f);
        [SerializeField] Color m_Running = new(0.24f, 0.62f, 0.96f, 1f);

        // One list per language, run in the selected language (the EN / 한국어 / العربية toggle). The lists hold the same
        // questions so a run looks the same in every language. They cover what Nova answers: chat, weather, the time in a
        // city, the user's location and web lookups.
        [Header("Questions (run in order, top to bottom, in the selected language)")]
        [Tooltip("English questions.")]
        [SerializeField]
        List<string> m_Commands = new()
        {
            "Hi Nova, who are you?",
            "How's the weather in Seoul?",
            "What time is it in New York?",
            "Where am I right now?",
            "Who painted the Mona Lisa?",
            "How tall is Mount Everest?",
            "What is the capital of Australia?",
            "Tell me a fun fact about octopuses.",
            "Can you recommend a good book for the weekend?",
            "Thank you, goodbye!",
        };

        [Tooltip("Korean questions.")]
        [SerializeField]
        List<string> m_CommandsKo = new()
        {
            "안녕 노바, 너는 누구야?",
            "서울 날씨 어때?",
            "뉴욕은 지금 몇 시야?",
            "나 지금 어디에 있어?",
            "모나리자는 누가 그렸어?",
            "에베레스트산 높이가 얼마야?",
            "호주의 수도는 어디야?",
            "문어에 대한 재미있는 사실 하나 알려줘.",
            "주말에 읽을 만한 좋은 책 추천해줘.",
            "고마워, 안녕!",
        };

        [Tooltip("Arabic questions.")]
        [SerializeField]
        List<string> m_CommandsAr = new()
        {
            "مرحبا نوفا، من أنت؟",
            "كيف الطقس في سيول؟",
            "كم الساعة الآن في نيويورك؟",
            "أين أنا الآن؟",
            "من رسم الموناليزا؟",
            "كم يبلغ ارتفاع جبل إيفرست؟",
            "ما هي عاصمة أستراليا؟",
            "أخبرني بمعلومة ممتعة عن الأخطبوط.",
            "اقترح علي كتابا جيدا لعطلة نهاية الأسبوع.",
            "شكرا لك، مع السلامة!",
        };

        /// <summary>The question list for the selected language (English when that list is empty).</summary>
        List<string> Commands
        {
            get
            {
                var list = AppLanguage.Current switch { Lang.Ko => m_CommandsKo, Lang.Ar => m_CommandsAr, _ => m_Commands };
                return list != null && list.Count > 0 ? list : m_Commands;
            }
        }

        Coroutine m_Run;
        TMP_Text m_Label;
        Image m_ButtonImage;

        public bool IsRunning => m_Run != null;

        void Start()
        {
            if (m_ShowButton) BuildButton();
            if (m_AutoRun) StartRun(m_StartDelay);
            else UpdateButton();
        }

        /// <summary>Start the question list if it isn't running, otherwise stop it.</summary>
        public void Toggle()
        {
            if (IsRunning) StopRun();
            else StartRun(0f);
        }

        public void StartRun(float delay)
        {
            if (IsRunning) return;
            m_Run = StartCoroutine(RunWhenReady(delay));
            UpdateButton();
        }

        public void StopRun()
        {
            if (!IsRunning) return;
            StopCoroutine(m_Run);
            m_Run = null;
            Debug.Log("[DEMO] STOPPED");
            UpdateButton();
        }

        IEnumerator RunWhenReady(float delay)
        {
            // Nova ignores a turn submitted before its models are loaded, so wait for it first.
            var nova = FindAnyObjectByType<NovaAvatarDemo>();
            while (nova == null || !nova.IsReady)
            {
                if (nova == null) nova = FindAnyObjectByType<NovaAvatarDemo>();
                yield return null;
            }
            if (delay > 0f) yield return new WaitForSeconds(delay);

            do yield return Run(nova);
            while (m_Loop);

            m_Run = null;
            UpdateButton();
        }

        /// <summary>Submit every question in order, each after the previous reply has been spoken.</summary>
        public IEnumerator Run(NovaAvatarDemo nova)
        {
            // The list is read again each step, so switching the language mid-run continues in the new language.
            for (var i = 0; i < Commands.Count; i++)
            {
                yield return WaitUntilIdle(nova);
                var commands = Commands;
                if (i >= commands.Count) break;
                var question = commands[i];
                if (string.IsNullOrWhiteSpace(question)) continue;

                Debug.Log($"[DEMO] {i + 1}/{commands.Count}  {question}");
                nova.SubmitText(question);

                // Let the turn start before checking whether it is still running.
                yield return null;
                yield return null;
                yield return WaitUntilIdle(nova);
                if (m_Interval > 0f) yield return new WaitForSeconds(m_Interval);
            }

            yield return WaitUntilIdle(nova);
            Debug.Log("[DEMO] DONE");
        }

        static IEnumerator WaitUntilIdle(NovaAvatarDemo nova)
        {
            // Idle for a few frames in a row: the reply text is done before its speech starts, so one free frame isn't enough.
            for (var idle = 0; idle < 5; idle = nova.IsBusy ? 0 : idle + 1)
                yield return null;
        }

        void BuildButton()
        {
            var canvasGo = new GameObject("DemoButtonCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = m_SortingOrder;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var buttonGo = new GameObject("DemoButton", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonGo.transform.SetParent(canvasGo.transform, false);
            var rt = (RectTransform)buttonGo.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = m_Corner;
            rt.anchoredPosition = m_Offset;
            rt.sizeDelta = m_Size;
            m_ButtonImage = buttonGo.GetComponent<Image>();
            m_ButtonImage.sprite = UiRuntimeAssets.RoundedSprite(16f);
            m_ButtonImage.type = Image.Type.Sliced;
            buttonGo.GetComponent<Button>().onClick.AddListener(Toggle);

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(buttonGo.transform, false);
            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            m_Label = textGo.GetComponent<TMP_Text>();
            m_Label.fontSize = m_FontSize;
            m_Label.alignment = TextAlignmentOptions.Center;
            m_Label.color = Color.white;
            m_Label.raycastTarget = false;
            m_Label.textWrappingMode = TextWrappingModes.NoWrap;
        }

        void UpdateButton()
        {
            if (m_Label != null) m_Label.text = IsRunning ? m_RunningLabel : m_IdleLabel;
            if (m_ButtonImage != null) m_ButtonImage.color = IsRunning ? m_Running : m_Background;
        }
    }
}
