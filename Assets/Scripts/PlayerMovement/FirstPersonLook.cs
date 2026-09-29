using UnityEngine;

// Yaw belongs to the character body; pitch belongs to the view pivot.
public sealed class FirstPersonLook : MonoBehaviour
{
    [SerializeField] private Transform yawRoot;
    [SerializeField] private float mouseSensitivity = 0.08f;
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    private float pitch;

    private void Awake()
    {
        pitch = Mathf.DeltaAngle(0f, transform.localEulerAngles.x);
    }

    public void ApplyLook(Vector2 mouseDelta)
    {
        if (yawRoot == null)
            return;

        yawRoot.Rotate(0f, mouseDelta.x * mouseSensitivity, 0f);
        pitch = Mathf.Clamp(pitch - mouseDelta.y * mouseSensitivity, pitchMin, pitchMax);
        transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }
}
