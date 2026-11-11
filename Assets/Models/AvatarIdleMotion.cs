using System;
using UnityEngine;

/// <summary>
/// Procedural idle motion for a humanoid rig with no Animator (e.g. the Avaturn glb): breathing in the spine and
/// shoulders, a slow sway of the hips and spine, and a head that drifts and now and then glances somewhere new.
/// Motion is layered on top of the bones' rest pose each LateUpdate, in the avatar root's axes, so it works whatever
/// the individual bones' local axes are. While the face is talking (<see cref="BlendshapeManager.IsPlaying"/>) the
/// head moves a little more.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class AvatarIdleMotion : MonoBehaviour
{
    [Tooltip("Root of the avatar rig. If empty, the root of the BlendshapeManager's head mesh is used.")]
    [SerializeField] Transform m_Avatar;
    [Tooltip("Optional: the head moves a bit more while this is playing lip sync.")]
    [SerializeField] BlendshapeManager m_Blendshapes;

    [Header("Breathing")]
    [SerializeField] float m_BreathsPerMinute = 14f;
    [Tooltip("Degrees the chest tilts back on each breath.")]
    [SerializeField] float m_BreathChest = 1.2f;
    [Tooltip("Degrees the shoulders lift on each breath.")]
    [SerializeField] float m_BreathShoulders = 1.5f;

    [Header("Body sway")]
    [Tooltip("Degrees of slow sway (pitch, yaw, roll) of the hips/spine.")]
    [SerializeField] Vector3 m_BodySway = new Vector3(1.0f, 1.5f, 1.2f);
    [SerializeField] float m_BodySwaySpeed = 0.12f;

    [Header("Head")]
    [Tooltip("Degrees of continuous drift (pitch, yaw, roll) of the head and neck.")]
    [SerializeField] Vector3 m_HeadDrift = new Vector3(2.0f, 3.0f, 1.5f);
    [SerializeField] float m_HeadDriftSpeed = 0.25f;
    [Tooltip("Largest glance (pitch, yaw) in degrees. Glances happen every few seconds.")]
    [SerializeField] Vector2 m_GlanceRange = new Vector2(3f, 6f);
    [SerializeField] Vector2 m_GlanceInterval = new Vector2(2.5f, 6f);
    [SerializeField] float m_GlanceSmoothing = 3f;
    [Tooltip("Head drift multiplier while talking.")]
    [SerializeField] float m_TalkingBoost = 1.6f;

    [Header("General")]
    [Range(0f, 2f)] [SerializeField] float m_Intensity = 1f;

    struct Bone
    {
        public Transform transform;
        public Quaternion rest;
    }

    Bone m_Hips, m_Spine, m_Spine1, m_Spine2, m_Neck, m_Head, m_LeftShoulder, m_RightShoulder;
    Bone[] m_All;
    Vector2 m_Glance, m_GlanceTarget;
    float m_NextGlance, m_Talk, m_Seed;

    void Awake()
    {
        if (m_Avatar == null && m_Blendshapes != null && m_Blendshapes.SkinnedMesh != null)
            m_Avatar = m_Blendshapes.SkinnedMesh.transform.root;
        if (m_Avatar == null)
        {
            Debug.LogWarning("[IdleMotion] No avatar assigned.", this);
            enabled = false;
            return;
        }

        var all = m_Avatar.GetComponentsInChildren<Transform>(true);
        m_Hips = Find(all, "Hips");
        m_Spine = Find(all, "Spine");
        m_Spine1 = Find(all, "Spine1");
        m_Spine2 = Find(all, "Spine2");
        m_Neck = Find(all, "Neck");
        m_Head = Find(all, "Head");
        m_LeftShoulder = Find(all, "LeftShoulder");
        m_RightShoulder = Find(all, "RightShoulder");
        // Parents before children, so each bone's offset is applied on top of its already-moved parent.
        m_All = new[] { m_Hips, m_Spine, m_Spine1, m_Spine2, m_LeftShoulder, m_RightShoulder, m_Neck, m_Head };

        if (m_Head.transform == null)
            Debug.LogWarning($"[IdleMotion] No 'Head' bone under {m_Avatar.name}; head motion is off.", this);

        m_Seed = UnityEngine.Random.value * 100f;
        m_NextGlance = Time.time + 1f;
    }

    void OnDisable()
    {
        // Put the rig back in its rest pose.
        if (m_All == null) return;
        foreach (var b in m_All)
            if (b.transform != null) b.transform.localRotation = b.rest;
    }

    void LateUpdate()
    {
        var t = Time.time;
        var dt = Time.deltaTime;
        var k = m_Intensity;

        var talking = m_Blendshapes != null && m_Blendshapes.IsPlaying;
        m_Talk = Mathf.MoveTowards(m_Talk, talking ? 1f : 0f, dt * 2f);
        var headScale = k * Mathf.Lerp(1f, m_TalkingBoost, m_Talk);

        // Breathing: a slightly asymmetric wave (quicker in, slower out), 0..1.
        var phase = t * m_BreathsPerMinute / 60f * Mathf.PI * 2f;
        var breath = 0.5f + 0.5f * Mathf.Sin(phase + 0.25f * Mathf.Sin(phase));

        // Body sway: slow noise, spread over the hips and spine so it bends rather than pivots.
        var sway = Scale(Noise3(t * m_BodySwaySpeed, m_Seed), m_BodySway) * k;

        // Head: continuous drift plus an occasional glance that eases toward a new target.
        if (t >= m_NextGlance)
        {
            m_GlanceTarget = new Vector2(UnityEngine.Random.Range(-1f, 1f) * m_GlanceRange.x,
                                         UnityEngine.Random.Range(-1f, 1f) * m_GlanceRange.y);
            // Often come back toward the camera instead of wandering further.
            if (UnityEngine.Random.value < 0.4f) m_GlanceTarget *= 0.2f;
            m_NextGlance = t + UnityEngine.Random.Range(m_GlanceInterval.x, m_GlanceInterval.y);
        }
        m_Glance = Vector2.Lerp(m_Glance, m_GlanceTarget, 1f - Mathf.Exp(-m_GlanceSmoothing * dt));
        var head = (Scale(Noise3(t * m_HeadDriftSpeed, m_Seed + 37f), m_HeadDrift)
                    + new Vector3(m_Glance.x, m_Glance.y, 0f)) * headScale;

        var chest = -m_BreathChest * breath * k;
        var shoulders = m_BreathShoulders * breath * k;

        foreach (var b in m_All)
            if (b.transform != null) b.transform.localRotation = b.rest;

        Rotate(m_Hips, 0f, sway.y * 0.3f, sway.z * 0.3f);
        Rotate(m_Spine, sway.x * 0.3f + chest * 0.2f, sway.y * 0.2f, sway.z * 0.3f);
        Rotate(m_Spine1, sway.x * 0.3f + chest * 0.3f, sway.y * 0.25f, sway.z * 0.2f);
        Rotate(m_Spine2, sway.x * 0.4f + chest * 0.5f, sway.y * 0.25f, sway.z * 0.2f);
        // Shoulders lift: roll each one up around the forward axis (opposite signs on either side).
        Rotate(m_LeftShoulder, 0f, 0f, -shoulders);
        Rotate(m_RightShoulder, 0f, 0f, shoulders);
        // Neck takes some of the head motion and cancels part of the chest's breathing tilt so the face stays level.
        Rotate(m_Neck, head.x * 0.35f - chest * 0.5f, head.y * 0.35f, head.z * 0.35f);
        Rotate(m_Head, head.x * 0.65f - chest * 0.3f, head.y * 0.65f, head.z * 0.65f);
    }

    // Rotates a bone by (pitch, yaw, roll) degrees around the avatar root's right, up and forward axes.
    void Rotate(Bone bone, float pitch, float yaw, float roll)
    {
        if (bone.transform == null) return;
        var root = m_Avatar.rotation;
        var offset = root * Quaternion.Euler(pitch, yaw, roll) * Quaternion.Inverse(root);
        bone.transform.rotation = offset * bone.transform.rotation;
    }

    static Bone Find(Transform[] all, string name)
    {
        var t = Array.Find(all, x => x.name == name);
        return new Bone { transform = t, rest = t != null ? t.localRotation : Quaternion.identity };
    }

    // Smooth noise in -1..1 per axis, two octaves so it doesn't look like a sine wave.
    static Vector3 Noise3(float t, float seed)
    {
        return new Vector3(Octaves(t, seed), Octaves(t, seed + 11.3f), Octaves(t, seed + 23.7f));
    }

    static float Octaves(float t, float seed)
    {
        var a = Mathf.PerlinNoise(t, seed) * 2f - 1f;
        var b = Mathf.PerlinNoise(t * 2.3f, seed + 5f) * 2f - 1f;
        return Mathf.Clamp(a * 0.75f + b * 0.25f, -1f, 1f) * 1.4f;
    }

    static Vector3 Scale(Vector3 a, Vector3 b) => Vector3.Scale(a, b);
}
