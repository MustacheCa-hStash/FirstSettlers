using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float walkSpeed = 2.5f;
    [SerializeField] float sprintSpeed = 4.5f;
    [SerializeField] float crouchSpeed = 1.5f;
    [SerializeField] float acceleration = 12f;

    [Header("Look")]
    [SerializeField] float mouseSensitivity = 0.08f;
    [SerializeField] float pitchMin = -80f;
    [SerializeField] float pitchMax = 80f;

    [Header("Gravity / Jump")]
    [SerializeField] float gravity = -20f;
    [SerializeField] float jumpHeight = 1.1f;

    [Header("Crouch")]
    [SerializeField] float standingHeight = 1.8f;
    [SerializeField] float crouchingHeight = 1.2f;
    [SerializeField] float heightLerpSpeed = 12f;
    [SerializeField] float standingCameraY = 1.6f;
    [SerializeField] float crouchingCameraY = 1.15f;

    CharacterController controller;
    Camera playerCamera;

    Vector3 horizontalVelocity;
    float verticalVelocity;

    float pitch;
    bool isCrouching;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>();

        controller.height = standingHeight;
        controller.center = new Vector3(0f, standingHeight * 0.5f, 0f);

        var camLocal = playerCamera.transform.localPosition;
        camLocal.x = 0f;
        camLocal.y = standingCameraY;
        camLocal.z = 0f;
        playerCamera.transform.localPosition = camLocal;
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleLook();
        HandleMove();
        HandleCrouch();
    }

    void HandleLook()
    {
        if (Mouse.current == null) return;

        Vector2 delta = Mouse.current.delta.ReadValue();
        float yaw = delta.x * mouseSensitivity;
        float lookY = delta.y * mouseSensitivity;

        transform.Rotate(Vector3.up * yaw);

        pitch -= lookY;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void HandleMove()
    {
        if (Keyboard.current == null) return;

        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        if (input.sqrMagnitude > 1f) input.Normalize();

        bool sprint = Keyboard.current.leftShiftKey.isPressed && !isCrouching;
        float targetSpeed = isCrouching ? crouchSpeed : (sprint ? sprintSpeed : walkSpeed);

        Vector3 desired = (transform.right * input.x + transform.forward * input.y) * targetSpeed;

        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            desired,
            acceleration * Time.deltaTime
        );

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        if (controller.isGrounded && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        controller.Move(motion);
    }

    void HandleCrouch()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.leftCtrlKey.wasPressedThisFrame)
            isCrouching = !isCrouching;

        float targetHeight = isCrouching ? crouchingHeight : standingHeight;
        controller.height = Mathf.Lerp(controller.height, targetHeight, heightLerpSpeed * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

        float targetCamY = isCrouching ? crouchingCameraY : standingCameraY;
        Vector3 camLocal = playerCamera.transform.localPosition;
        camLocal.y = Mathf.Lerp(camLocal.y, targetCamY, heightLerpSpeed * Time.deltaTime);
        playerCamera.transform.localPosition = camLocal;
    }
}