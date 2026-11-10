using UnityEngine;

/// <summary>
/// Finds blendshapes on a mesh by ARKit name regardless of the naming style the avatar was exported with.
/// These all resolve to the same blendshape:
///   "mouthSmile_L" (this project), "mouthSmileLeft" (Apple / Avaturn), "Mouth_Smile_L" (CC4-style),
///   "blendShape1.mouthSmileLeft" (FBX/glTF prefix), and any casing of those.
/// Also bridges the project's "eyeBlinking_*" and "mouthClosed" to ARKit's "eyeBlink*" and "mouthClose".
/// </summary>
public static class BlendshapeNames
{
    /// <summary>Index of the blendshape matching <paramref name="name"/>, or -1. An exact match wins.</summary>
    public static int FindIndex(Mesh mesh, string name)
    {
        if (mesh == null || string.IsNullOrEmpty(name)) return -1;

        var exact = mesh.GetBlendShapeIndex(name);
        if (exact >= 0) return exact;

        var key = Normalize(name);
        for (var i = 0; i < mesh.blendShapeCount; i++)
            if (Normalize(mesh.GetBlendShapeName(i)) == key)
                return i;
        return -1;
    }

    /// <summary>
    /// Reduces a blendshape name to a style-independent key: drops any "prefix." part, case, underscores and spaces,
    /// and shortens a trailing Left/Right to l/r.
    /// </summary>
    public static string Normalize(string name)
    {
        var dot = name.LastIndexOf('.');
        if (dot >= 0) name = name.Substring(dot + 1);

        var key = name.Replace("_", "").Replace(" ", "").ToLowerInvariant();

        if (key.EndsWith("left")) key = key.Substring(0, key.Length - 4) + "l";
        else if (key.EndsWith("right")) key = key.Substring(0, key.Length - 5) + "r";

        if (key.StartsWith("eyeblinking")) key = "eyeblink" + key.Substring("eyeblinking".Length);
        else if (key == "mouthclosed") key = "mouthclose";

        return key;
    }
}
