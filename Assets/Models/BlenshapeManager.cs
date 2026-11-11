using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

public class BlendshapeManager : MonoBehaviour
{
    /// <summary>Which blendshape each of NeuroSync's 52 face values drives.</summary>
    public enum ChannelMapping
    {
        /// <summary>The original demo's mapping, tuned for SampleHead.</summary>
        SampleHead,
        /// <summary>Apple's ARKit order as recorded by Live Link Face, for avatars with standard ARKit blendshapes.</summary>
        LiveLinkArkit,
    }

    [SerializeField] private string audioToBlendshapeUrl = "http://localhost:5005/audio_to_blendshapes";
    [SerializeField] private SkinnedMeshRenderer m_SkinnedMesh;
    [Tooltip("Other face meshes that share ARKit blendshapes with the head, such as eyes, eyelashes, teeth and tongue. " +
             "If empty, every SkinnedMeshRenderer with blendshapes under the head's root object is driven too.")]
    [SerializeField] private SkinnedMeshRenderer[] m_ExtraFaceMeshes;
    [SerializeField] private float m_BlendShapeScale = 110f;
    [Tooltip("Which blendshape each NeuroSync value drives. SampleHead: the original demo's mapping. " +
             "Live Link ARKit: the standard ARKit order, for avatars with ARKit blendshapes such as Avaturn or CC4.")]
    [SerializeField] private ChannelMapping m_ChannelMapping = ChannelMapping.SampleHead;

    private const int TARGET_FRAMERATE = 60;
    private const int ARKIT_BLENDSHAPE_COUNT = 52;

    private bool isPlaying = false;
    private List<BlendshapeFrame> blendshapeFrames = new List<BlendshapeFrame>();
    private AudioSource audioSource;
    private float playbackTime = 0f;

    // The original demo's mapping from NeuroSync output index to SampleHead's blendshape names.
    private static readonly string[] sampleHeadNames = new string[]
    {
        "eyeBlinking_Left", "eyeBlinking_Right", "eyeLookDown_L", "eyeLookDown_R", "eyeLookIn_L", "eyeLookIn_R", "eyeLookOut_L", "eyeLookOut_R", "eyeLookUp_L", "eyeLookUp_R", "eyeSquint_L", "eyeSquint_R", "eyeWide_Left", "eyeWide_Right", "jawForward", "jawLeft", "jawRight", "jawOpen", "mouthClosed", "mouthFunnel", "mouthPucker", "mouthLeft", "mouthRight", "mouthSmile_L", "mouthSmile_R", "mouthFrown_L", "mouthFrown_R", "mouthDimple_L", "mouthDimple_R", "mouthStretch_L", "mouthStretch_R", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper", "mouthPress_L", "mouthPress_R", "mouthLowerDown_L", "mouthLowerDown_R", "mouthUpperUp_L", "mouthUpperUp_R", "browDown_Left", "browDown_Right", "browInnerUp", "browOuterUp_L", "browOuterUp_R", "cheekPuff", "cheekSquint_L", "cheekSquint_R", "noseSneer_L", "noseSneer_R", "tongue_jawOpen", "tongue_jawForward", "tongue_jawLeft", "tongue_jawRight", "tongue_tongueOut",
    };

    // Live Link Face / ARKit order: left eye, right eye, jaw, mouth, brows, cheeks, nose, tongue.
    private static readonly string[] liveLinkArkitNames = new string[]
    {
        "eyeBlinkLeft", "eyeLookDownLeft", "eyeLookInLeft", "eyeLookOutLeft", "eyeLookUpLeft", "eyeSquintLeft", "eyeWideLeft",
        "eyeBlinkRight", "eyeLookDownRight", "eyeLookInRight", "eyeLookOutRight", "eyeLookUpRight", "eyeSquintRight", "eyeWideRight",
        "jawForward", "jawRight", "jawLeft", "jawOpen",
        "mouthClose", "mouthFunnel", "mouthPucker", "mouthRight", "mouthLeft",
        "mouthSmileLeft", "mouthSmileRight", "mouthFrownLeft", "mouthFrownRight", "mouthDimpleLeft", "mouthDimpleRight",
        "mouthStretchLeft", "mouthStretchRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
        "mouthPressLeft", "mouthPressRight", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthUpperUpLeft", "mouthUpperUpRight",
        "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
        "cheekPuff", "cheekSquintLeft", "cheekSquintRight", "noseSneerLeft", "noseSneerRight",
        "tongueOut",
    };

    private string[] blendshapeNames => m_ChannelMapping == ChannelMapping.LiveLinkArkit ? liveLinkArkitNames : sampleHeadNames;

    // Face meshes driven together (head first), each with a blendshape name → index cache. Rebuilt when the head changes.
    private readonly List<SkinnedMeshRenderer> faceMeshes = new List<SkinnedMeshRenderer>();
    private readonly List<Dictionary<string, int>> faceIndexCaches = new List<Dictionary<string, int>>();
    private SkinnedMeshRenderer faceMeshesHead;

    void Awake()
    {
        Application.targetFrameRate = TARGET_FRAMERATE;
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
    }

    public void SetBlendshapeDataAndPlay(List<float[]> blendshapes, AudioClip audioClip)
    {
        if (blendshapes == null || blendshapes.Count == 0)
        {
            Debug.LogError("No blendshape data received.", this);
            return;
        }
        
        blendshapeFrames = ConvertToBlendshapeFrames(blendshapes);
        audioSource.clip = audioClip;
        Play();
    }
    
    /// <summary>
    /// Plays blendshape frames on the face without touching audio, for audio that another component plays (TTS).
    /// Call it in the frame the audio starts.
    /// </summary>
    public void PlayBlendshapes(List<float[]> blendshapes)
    {
        if (blendshapes == null || blendshapes.Count == 0) return;
        blendshapeFrames = ConvertToBlendshapeFrames(blendshapes);
        playbackTime = 0f;
        isPlaying = true;
    }

    public bool IsPlaying => isPlaying;

    public SkinnedMeshRenderer SkinnedMesh => m_SkinnedMesh;

    void Update()
    {
        if (!isPlaying || blendshapeFrames.Count == 0)
        {
            return;
        }

        playbackTime += Time.deltaTime;

        int targetFrame = Mathf.FloorToInt(playbackTime * TARGET_FRAMERATE);

        if (targetFrame >= blendshapeFrames.Count)
        {
            Stop();
            return;
        }

        ApplyBlendshapes(blendshapeFrames[targetFrame]);
    }

    /// <summary>
    /// Sets a blendshape on every face mesh that has it. Accepts both this project's names ("mouthSmile_L") and ARKit
    /// standard names ("mouthSmileLeft"). <paramref name="weight"/> is in Unity's 0–100 range.
    /// </summary>
    public void SetWeight(string blendshapeName, float weight)
    {
        EnsureFaceMeshes();
        for (int m = 0; m < faceMeshes.Count; m++)
        {
            int index = IndexOn(m, blendshapeName);
            if (index != -1) faceMeshes[m].SetBlendShapeWeight(index, weight);
        }
    }

    /// <summary>Current weight of a blendshape on the head mesh, or 0 if the head doesn't have it.</summary>
    public float GetWeight(string blendshapeName)
    {
        EnsureFaceMeshes();
        if (faceMeshes.Count == 0 || faceMeshes[0] != m_SkinnedMesh) return 0f;
        int index = IndexOn(0, blendshapeName);
        return index != -1 ? m_SkinnedMesh.GetBlendShapeWeight(index) : 0f;
    }

    private int IndexOn(int meshSlot, string blendshapeName)
    {
        var cache = faceIndexCaches[meshSlot];
        if (!cache.TryGetValue(blendshapeName, out int index))
            cache[blendshapeName] = index = BlendshapeNames.FindIndex(faceMeshes[meshSlot].sharedMesh, blendshapeName);
        return index;
    }

    private void EnsureFaceMeshes()
    {
        if (faceMeshesHead == m_SkinnedMesh && faceMeshes.Count > 0) return;
        faceMeshesHead = m_SkinnedMesh;
        faceMeshes.Clear();
        faceIndexCaches.Clear();
        if (m_SkinnedMesh == null) return;

        void Add(SkinnedMeshRenderer r)
        {
            if (r == null || r.sharedMesh == null || r.sharedMesh.blendShapeCount == 0 || faceMeshes.Contains(r)) return;
            faceMeshes.Add(r);
            faceIndexCaches.Add(new Dictionary<string, int>());
        }

        Add(m_SkinnedMesh);
        if (m_ExtraFaceMeshes != null && m_ExtraFaceMeshes.Length > 0)
            foreach (var r in m_ExtraFaceMeshes) Add(r);
        else
            foreach (var r in m_SkinnedMesh.transform.root.GetComponentsInChildren<SkinnedMeshRenderer>(true)) Add(r);

        var missing = new List<string>();
        for (int i = 0; i < ARKIT_BLENDSHAPE_COUNT; i++)
        {
            bool found = false;
            for (int m = 0; m < faceMeshes.Count && !found; m++) found = IndexOn(m, blendshapeNames[i]) != -1;
            if (!found) missing.Add(blendshapeNames[i]);
        }

        var meshNames = faceMeshes.ConvertAll(r => r.name);
        Debug.Log($"[Blendshapes] Driving {faceMeshes.Count} face mesh(es): {string.Join(", ", meshNames)}", this);
        if (missing.Count > 0)
            Debug.LogWarning($"[Blendshapes] No face mesh has {missing.Count} of {ARKIT_BLENDSHAPE_COUNT} ARKit blendshapes: {string.Join(", ", missing)}", this);
    }

    private void ApplyBlendshapes(BlendshapeFrame frame)
    {
        foreach (var kvp in frame.values)
        {
            float value = kvp.Value;

            //Note: Due to the difference in eyeWide_Left, we set it to follow the eyeWide_Right value.
            if (kvp.Key == "eyeWide_Left")
                frame.values.TryGetValue("eyeWide_Right", out value);

            SetWeight(kvp.Key, value * m_BlendShapeScale);
        }
    }

    private void Play()
    {
        if (blendshapeFrames.Count == 0)
        {
            Debug.LogWarning("No blendshape data loaded!");
            return;
        }

        if (audioSource != null && audioSource.clip != null)
        {
            playbackTime = 0f;
            audioSource.Play();
            isPlaying = true;
        }
    }

    public void Stop()
    {
        isPlaying = false;
        playbackTime = 0f;
        
        EnsureFaceMeshes();
        foreach (var faceMesh in faceMeshes)
        {
            for (int i = 0; i < faceMesh.sharedMesh.blendShapeCount; i++)
            {
                faceMesh.SetBlendShapeWeight(i, 0);
            }
        }

        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.Stop();
        }
    }
    
    private List<BlendshapeFrame> ConvertToBlendshapeFrames(List<float[]> blendshapes)
    {
        List<BlendshapeFrame> frames = new List<BlendshapeFrame>();

        foreach (var blendshapeArray in blendshapes)
        {
            BlendshapeFrame frame = new BlendshapeFrame();
            for (int i = 0; i < ARKIT_BLENDSHAPE_COUNT && i < blendshapeArray.Length; i++)
            {
                frame.values[blendshapeNames[i]] = blendshapeArray[i];
            }
            frames.Add(frame);
        }

        if (blendshapes.Count > 0)
        {
            var lastBlendshapeArray = blendshapes[^1];
            const int FADE_OUT_FRAMES = 20;

            for (int i = 0; i < FADE_OUT_FRAMES; i++)
            {
                var fadeOutFrame = new BlendshapeFrame();
                float fadeFactor = 1.0f - ((float)i / FADE_OUT_FRAMES);

                for (int j = 0; j < ARKIT_BLENDSHAPE_COUNT && j < lastBlendshapeArray.Length; j++)
                {
                    fadeOutFrame.values[blendshapeNames[j]] = lastBlendshapeArray[j] * fadeFactor;
                }
                frames.Add(fadeOutFrame);
            }
        }

        return frames;
    }

    private class BlendshapeFrame
    {
        public Dictionary<string, float> values = new Dictionary<string, float>();
    }
    
    #region Unused Server Communication Code
    
    private class BlendshapesResponse
    {
        public List<float[]> blendshapes { get; set; }
    }

    public async Task WavToBlendshape(AudioClip audioClip)
    {
        byte[] wavData = AudioClipToWav(audioClip);

        using (UnityWebRequest request = new UnityWebRequest(audioToBlendshapeUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(wavData);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "audio/wav");

            try
            {
                var asyncOp = request.SendWebRequest();
                while (!asyncOp.isDone)
                {
                    await Task.Yield();
                }

                if (request.result == UnityWebRequest.Result.Success)
                {
                    string jsonResponse = request.downloadHandler.text;
                    var response = JsonConvert.DeserializeObject<BlendshapesResponse>(jsonResponse);
                    SetBlendshapeDataAndPlay(response.blendshapes, audioClip);
                }
                else
                {
                    Debug.LogError($"API Error: {request.error}");
                    Debug.LogError($"Response Code: {request.responseCode}");
                    Debug.LogError($"Response: {request.downloadHandler.text}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Exception occurred: {e.Message}");
            }
        }
    }
    
    private byte[] AudioClipToWav(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);

        using (MemoryStream stream = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(0);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)clip.channels);
                writer.Write(clip.frequency);
                writer.Write(clip.frequency * clip.channels * 2);
                writer.Write((short)(clip.channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Length * 2);

                foreach (float sample in samples)
                {
                    writer.Write((short)(sample * 32767));
                }

                stream.Position = 4;
                writer.Write((int)(stream.Length - 8));
            }
            return stream.ToArray();
        }
    }
    #endregion
}