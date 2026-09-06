using UnityEngine;
using UnityEngine.InputSystem;

public class VincentPlayerMovement : MonoBehaviour
{
    public float moveSpeed = 5f;
    public float sprintSpeed = 8f;
    public float rotationSpeed = 10f;
    public float gravity = -9.81f;

    public float dampTime = 0.1f;

    public Transform cameraTransform;

    private CharacterController controller;
    private Animator animator;

    private Vector3 velocity;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    void Update()
    {
        // Get WASD input
        Vector2 input = Vector2.zero;

        if (Keyboard.current.wKey.isPressed)
            input.y += 1;

        if (Keyboard.current.sKey.isPressed)
            input.y -= 1;

        if (Keyboard.current.dKey.isPressed)
            input.x += 1;

        if (Keyboard.current.aKey.isPressed)
            input.x -= 1;

        // Check if the player is sprinting
        bool sprinting = Keyboard.current.leftShiftKey.isPressed && input.y > 0;

        // Get the camera's forward and right directions
        Vector3 cameraForward = cameraTransform.forward;
        Vector3 cameraRight = cameraTransform.right;

        // Ignore the camera's vertical rotation
        cameraForward.y = 0;
        cameraRight.y = 0;

        cameraForward.Normalize();
        cameraRight.Normalize();

        // Create movement direction relative to the camera
        Vector3 move = cameraForward * input.y + cameraRight * input.x;

        // Rotate the player toward the movement direction
        if (move != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(move);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }

        float currentSpeed = sprinting ? sprintSpeed : moveSpeed;

        // Move the character
        controller.Move(move * currentSpeed * Time.deltaTime);

        // Apply gravity
        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;

        controller.Move(velocity * Time.deltaTime);

        // Use a larger Y value when sprinting
        float animationY = sprinting ? input.y * 2f : input.y;

        animator.SetFloat("move X", input.x, dampTime, Time.deltaTime);
        animator.SetFloat("move Y", animationY, dampTime, Time.deltaTime);
    }
}
