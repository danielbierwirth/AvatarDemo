using System;
using UnityEngine;

namespace VoiceAgent.Scenes
{
    /// <summary>
    /// Shows one avatar per voice style: each <see cref="Avatar"/> lists the GameObjects that make it up (the model and
    /// its lip sync controller) and the voice-name prefix it stands for, e.g. "F" for Supertonic's F1–F5 and "M" for
    /// M1–M5. <see cref="Show"/> activates the matching avatar and deactivates the others; a voice that matches none
    /// leaves the current one up.
    /// </summary>
    public sealed class AvatarSwitcher : MonoBehaviour
    {
        [Serializable]
        public sealed class Avatar
        {
            [Tooltip("Voice names starting with this (case-insensitive) use this avatar, e.g. F or M.")]
            public string VoicePrefix;
            [Tooltip("Everything that belongs to this avatar: the model root and its lip sync controller.")]
            public GameObject[] Objects;
        }

        [SerializeField] Avatar[] m_Avatars = Array.Empty<Avatar>();

        /// <summary>The avatar shown now, or null before the first <see cref="Show"/>.</summary>
        public Avatar Current { get; private set; }

        /// <summary>Shows the avatar for <paramref name="voice"/> (e.g. "M3"). Returns false when no avatar matches.</summary>
        public bool Show(string voice)
        {
            var match = Find(voice);
            if (match == null) return false;
            if (match == Current) return true;

            // Deactivate first, so the outgoing lip sync unsubscribes from the speech before the incoming one subscribes.
            foreach (var avatar in m_Avatars)
                if (avatar != match) SetActive(avatar, false);
            SetActive(match, true);
            Current = match;
            Debug.Log($"[Avatar] voice {voice} → {match.VoicePrefix} avatar");
            return true;
        }

        Avatar Find(string voice)
        {
            if (string.IsNullOrEmpty(voice)) return null;
            foreach (var avatar in m_Avatars)
                if (!string.IsNullOrEmpty(avatar.VoicePrefix) && voice.StartsWith(avatar.VoicePrefix, StringComparison.OrdinalIgnoreCase))
                    return avatar;
            return null;
        }

        static void SetActive(Avatar avatar, bool active)
        {
            foreach (var go in avatar.Objects)
                if (go != null && go.activeSelf != active) go.SetActive(active);
        }
    }
}
