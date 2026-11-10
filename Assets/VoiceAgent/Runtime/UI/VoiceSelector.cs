using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace VoiceAgent.UI
{
    /// <summary>
    /// The voice style picker: a captioned grid of segments (F1–F5 / M1–M5 with Supertonic 3) on its own overlay canvas,
    /// styled like <see cref="LanguageToggle"/>. <see cref="Show"/> builds it from the voices the TTS model ships;
    /// clicking one raises <see cref="Selected"/>. The highlighted segment is set with <see cref="Highlight"/>.
    /// </summary>
    public sealed class VoiceSelector : MonoBehaviour
    {
        [Header("Placement (reference resolution 1920 x 1080)")]
        [Tooltip("Anchor and pivot: (0,1) top-left, (0.5,1) top-center, (1,1) top-right.")]
        [SerializeField] Vector2 m_Corner = new(0f, 1f);
        [SerializeField] Vector2 m_Offset = new(32f, -168f);
        [Tooltip("Total width; matches the language toggle (3 x 96 + 8).")]
        [SerializeField] float m_Width = 296f;
        [SerializeField] float m_RowHeight = 40f;
        [SerializeField] int m_Columns = 5;
        [SerializeField] int m_FontSize = 20;
        [SerializeField] int m_SortingOrder = 1001;
        [SerializeField] string m_Caption = "VOICE STYLE";

        [SerializeField] Color m_Background = new(0.16f, 0.17f, 0.22f, 0.9f);
        [SerializeField] Color m_Selected = new(0.24f, 0.62f, 0.96f, 1f);
        [SerializeField] Color m_CaptionColor = new(0.62f, 0.66f, 0.74f, 1f);

        const float CaptionHeight = 26f, Padding = 4f;

        public event Action<string> Selected;

        readonly List<(string Voice, Image Segment)> m_Segments = new();
        GameObject m_Root;
        string m_Highlighted;

        /// <summary>Builds the picker for <paramref name="voices"/> (replacing an earlier one); hidden when there are none.</summary>
        public void Show(IReadOnlyList<string> voices, string highlighted)
        {
            if (m_Root != null) Destroy(m_Root);
            m_Segments.Clear();
            if (voices == null || voices.Count == 0) return;

            m_Root = new GameObject("VoiceSelectorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            m_Root.transform.SetParent(transform, false);
            var canvas = m_Root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = m_SortingOrder;
            var scaler = m_Root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var rows = Mathf.CeilToInt(voices.Count / (float)m_Columns);
            var panel = new GameObject("VoiceSelector", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(m_Root.transform, false);
            var rt = (RectTransform)panel.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = m_Corner;
            rt.anchoredPosition = m_Offset;
            rt.sizeDelta = new Vector2(m_Width, CaptionHeight + rows * m_RowHeight + Padding);
            var background = panel.GetComponent<Image>();
            background.sprite = UiRuntimeAssets.RoundedSprite(16f);
            background.type = Image.Type.Sliced;
            background.color = m_Background;

            var caption = NewText("Caption", panel.transform, m_Caption, 14, m_CaptionColor, TextAlignmentOptions.MidlineLeft);
            var crt = caption.rectTransform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = new Vector2(14f, -CaptionHeight);
            crt.offsetMax = new Vector2(-14f, 0f);
            caption.characterSpacing = 6f;

            var grid = new GameObject("Grid", typeof(RectTransform), typeof(GridLayoutGroup));
            grid.transform.SetParent(panel.transform, false);
            var grt = (RectTransform)grid.transform;
            grt.anchorMin = Vector2.zero;
            grt.anchorMax = Vector2.one;
            grt.offsetMin = new Vector2(Padding, Padding);
            grt.offsetMax = new Vector2(-Padding, -CaptionHeight);
            var layout = grid.GetComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2((m_Width - 2 * Padding) / m_Columns, m_RowHeight);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = m_Columns;

            foreach (var voice in voices)
            {
                var segment = new GameObject($"Voice_{voice}", typeof(RectTransform), typeof(Image), typeof(Button));
                segment.transform.SetParent(grid.transform, false);
                var image = segment.GetComponent<Image>();
                image.sprite = UiRuntimeAssets.RoundedSprite(12f);
                image.type = Image.Type.Sliced;
                var v = voice;
                segment.GetComponent<Button>().onClick.AddListener(() =>
                {
                    Highlight(v);
                    Selected?.Invoke(v);
                });
                var label = NewText("Label", segment.transform, voice, m_FontSize, Color.white, TextAlignmentOptions.Center);
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
                m_Segments.Add((voice, image));
            }
            Highlight(highlighted);
        }

        /// <summary>Marks <paramref name="voice"/> as the selected style (none when it isn't in the list).</summary>
        public void Highlight(string voice)
        {
            m_Highlighted = voice;
            foreach (var (v, image) in m_Segments)
                if (image != null) image.color = v == m_Highlighted ? m_Selected : new Color(1f, 1f, 1f, 0f);
        }

        static TMP_Text NewText(string name, Transform parent, string value, int size, Color color, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }
    }
}
