using System;
using UnityEngine;
using VoiceAgent;

/// <summary>
/// Drives the avatar's face from the voice dialog's TTS playback (<see cref="SpeechOutput"/>). Each synthesized sentence
/// is run through NeuroSync (<see cref="NeurosuncController.Generate"/>) right before it starts playing, and the resulting
/// ARKit blendshape frames play on the head in the same frame as the audio (<see cref="BlendshapeManager.PlayBlendshapes"/>).
/// If NeuroSync isn't available or fails, the jaw follows the loudness of the playing audio instead. The teeth mesh
/// (Skull) follows the head's jaw blendshapes.
/// </summary>
public sealed class AvatarLipSync : MonoBehaviour
{
    [SerializeField] SpeechOutput m_Speech;
    [SerializeField] NeurosuncController m_NeuroSync;
    [SerializeField] BlendshapeManager m_Blendshapes;
    [Tooltip("Teeth mesh whose teeth_jaw* blendshapes follow the head's jaw* blendshapes. Optional.")]
    [SerializeField] SkinnedMeshRenderer m_Teeth;

    [Header("Fallback (no NeuroSync): loudness → jawOpen")]
    [SerializeField] float m_JawGain = 900f;
    [SerializeField, Range(0f, 100f)] float m_MaxJawOpen = 45f;
    [SerializeField] float m_JawSmoothing = 18f;

    static readonly string[] s_Jaw = { "jawOpen", "jawLeft", "jawRight", "jawForward" };
    readonly int[] m_HeadJaw = new int[s_Jaw.Length];
    readonly int[] m_TeethJaw = new int[s_Jaw.Length];
    readonly float[] m_Samples = new float[512];
    bool m_Amplitude, m_NeuroSyncFailed;
    float m_Jaw;

    void Awake()
    {
        var head = m_Blendshapes != null ? m_Blendshapes.SkinnedMesh : null;
        for (var i = 0; i < s_Jaw.Length; i++)
        {
            m_HeadJaw[i] = head != null ? m_Blendshapes.GetBlendshapeIndex(s_Jaw[i]) : -1;
            m_TeethJaw[i] = m_Teeth != null ? BlendshapeNames.FindIndex(m_Teeth.sharedMesh, "teeth_" + s_Jaw[i]) : -1;
        }
    }

    void OnEnable()
    {
        if (m_Speech == null) return;
        m_Speech.ClipStarting += OnClipStarting;
        m_Speech.Stopped += OnStopped;
    }

    void OnDisable()
    {
        if (m_Speech == null) return;
        m_Speech.ClipStarting -= OnClipStarting;
        m_Speech.Stopped -= OnStopped;
    }

    void OnClipStarting(AudioClip clip)
    {
        m_Amplitude = false;
        if (m_NeuroSync != null && m_Blendshapes != null && !m_NeuroSyncFailed)
        {
            try
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var frames = m_NeuroSync.Generate(clip);
                if (frames != null && frames.Count > 0)
                {
                    m_Blendshapes.PlayBlendshapes(frames);
                    Debug.Log($"[LipSync] NeuroSync {frames.Count} frames for {clip.length:F1} s in {clock.Elapsed.TotalMilliseconds:F0} ms");
                    return;
                }
            }
            catch (Exception e)
            {
                // Don't retry every sentence: fall back to loudness for the rest of the session.
                m_NeuroSyncFailed = true;
                Debug.LogWarning($"[LipSync] NeuroSync failed, using loudness-driven jaw instead: {e.Message}");
            }
        }
        m_Amplitude = true;
    }

    void OnStopped()
    {
        if (!this) return;
        m_Amplitude = false;
        if (m_Blendshapes != null && m_Blendshapes.IsPlaying) m_Blendshapes.Stop();
    }

    void LateUpdate()
    {
        var head = m_Blendshapes != null ? m_Blendshapes.SkinnedMesh : null;
        if (head == null) return;

        if (m_Amplitude)
        {
            var source = m_Speech.Source;
            var target = 0f;
            if (source != null && source.isPlaying)
            {
                source.GetOutputData(m_Samples, 0);
                var sum = 0f;
                foreach (var s in m_Samples) sum += s * s;
                target = Mathf.Min(m_MaxJawOpen, Mathf.Sqrt(sum / m_Samples.Length) * m_JawGain);
            }
            m_Jaw = Mathf.Lerp(m_Jaw, target, 1f - Mathf.Exp(-m_JawSmoothing * Time.deltaTime));
            if (m_HeadJaw[0] >= 0) head.SetBlendShapeWeight(m_HeadJaw[0], m_Jaw);
            if (source == null || (!source.isPlaying && m_Jaw < 0.1f)) m_Amplitude = false;
        }

        if (m_Teeth == null) return;
        for (var i = 0; i < s_Jaw.Length; i++)
            if (m_HeadJaw[i] >= 0 && m_TeethJaw[i] >= 0)
                m_Teeth.SetBlendShapeWeight(m_TeethJaw[i], head.GetBlendShapeWeight(m_HeadJaw[i]));
    }
}
