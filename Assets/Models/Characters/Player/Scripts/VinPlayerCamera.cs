using UnityEngine;
using UnityEngine.InputSystem;

public class VinPlayerCamera : MonoBehaviour
{
    public Transform player;

    public float distance = 6f;
    public float height = 3f;

    public float mouseSensitivity = 0.2f;

    private float yaw = 0f;
    private float pitch = 20f;

    void LateUpdate()
    {
        // Read mouse movement
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        // Horizontal camera rotation
        yaw += mouseDelta.x * mouseSensitivity;

        // Vertical camera rotation
        pitch -= mouseDelta.y * mouseSensitivity;

        // Limit how far we can look up/down
        pitch = Mathf.Clamp(pitch, -10f, 60f);

        // Create the camera rotation
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        // Camera target point above the player
        Vector3 target = player.position + Vector3.up * height;

        // Position the camera behind the target
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);

        transform.position = target + offset;

        // Look at the player
        transform.LookAt(target);
    }

}
