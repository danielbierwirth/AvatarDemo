using UnityEngine;

/// <summary>
/// Keeps a quad (Unity's built-in 1x1 Quad mesh) filling a camera's view at a fixed distance, so a painted studio
/// backdrop always covers the screen whatever the aspect ratio. The quad is scaled slightly past the frustum edges
/// (<see cref="m_Overscan"/>) so no seam shows. Runs in edit mode too, so the Scene and Game views match.
/// </summary>
[ExecuteAlways]
public sealed class BackdropFitter : MonoBehaviour
{
    [Tooltip("Camera to fill. If empty, Camera.main is used.")]
    [SerializeField] Camera m_Camera;
    [Tooltip("Distance in front of the camera, in metres. Keep it behind the avatar.")]
    [SerializeField] float m_Distance = 4f;
    [Tooltip("How far past the frustum edges the quad extends (1 = exact fit).")]
    [SerializeField] float m_Overscan = 1.02f;

    void LateUpdate()
    {
        var cam = m_Camera != null ? m_Camera : Camera.main;
        if (cam == null)
            return;

        var t = cam.transform;
        transform.SetPositionAndRotation(t.position + t.forward * m_Distance, t.rotation);

        float height = 2f * m_Distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * m_Overscan;
        float width = height * cam.aspect;
        var parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3(width / parentScale.x, height / parentScale.y, 1f / parentScale.z);
    }
}
