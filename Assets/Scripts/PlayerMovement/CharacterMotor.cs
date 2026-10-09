using UnityEngine;

// Owns locomotion rules and collision resolution, without reading devices or animating visuals.
[RequireComponent(typeof(CharacterController))]
public sealed class CharacterMotor : MonoBehaviour
{
    [Header("Collision Queries")]
    [Tooltip("Solid surfaces considered by grounding and crouch headroom checks: terrain, rocks, trunks and solid props. Excludes Player and QueryOnly.")]
    [SerializeField] private LayerMask solidSurfaceMask = GameplayLayers.SolidSurfaceMask;

    [Header("Ground Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 7f;
    [SerializeField] private float crouchSpeed = 2.25f;
    [SerializeField] private float groundAcceleration = 65f;
    [SerializeField] private float groundBraking = 90f;
    [SerializeField] private float groundReverseAcceleration = 120f;
    [SerializeField] private float airAcceleration = 8f;

    [Header("Uphill Speed")]
    [SerializeField] private float slowdownStartsAtDegrees = 15f;
    [SerializeField] private float fullSlowdownAtDegrees = 50f;
    [Range(0f, 0.9f)]
    [SerializeField] private float maximumUphillSlowdown = 0.4f;

    [Header("Jump and Gravity")]
    [SerializeField] private float gravity = -25f;
    [SerializeField] private float jumpHeight = 1.25f;
    [SerializeField] private float groundStickSpeed = 2f;

    [Header("Stance")]
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchingHeight = 1.2f;
    [SerializeField] private float standingEyeHeight = 1.6f;
    [SerializeField] private float crouchingEyeHeight = 1.1f;
    [SerializeField] private Transform viewPivot;

    private readonly RaycastHit[] groundHits = new RaycastHit[8];
    private readonly Collider[] headroomHits = new Collider[16];
    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool groundUsesSmoothRamp;

    public Vector3 ActualVelocity { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public bool IsGrounded => controller != null && controller.isGrounded;
    public bool IsCrouched { get; private set; }

    private void Awake()
    {
        gameObject.layer = GameplayLayers.Player;
        GameplayLayers.AssignPhysicalColliders(gameObject, GameplayLayers.Player);
        controller = GetComponent<CharacterController>();
        SetStance(false);
    }

    public void Simulate(CharacterMoveCommand command, float deltaTime)
    {
        if (controller == null || deltaTime <= 0f)
            return;

        bool crouch = command.CrouchHeld || (IsCrouched && !CanStand());
        if (crouch != IsCrouched)
            SetStance(crouch);

        Vector2 input = Vector2.ClampMagnitude(command.Move, 1f);
        Vector3 direction = transform.right * input.x + transform.forward * input.y;
        float speed = IsCrouched ? crouchSpeed : command.SprintHeld ? sprintSpeed : walkSpeed;
        if (controller.isGrounded && input.sqrMagnitude > 0f)
            speed *= UphillSpeedFactor(direction.normalized);

        Vector3 targetVelocity = direction * speed;
        float responsiveness = airAcceleration;
        if (controller.isGrounded)
        {
            responsiveness = input.sqrMagnitude == 0f ? groundBraking : groundAcceleration;
            if (Vector3.Dot(horizontalVelocity, targetVelocity) < 0f)
                responsiveness = groundReverseAcceleration;
        }
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, responsiveness * deltaTime);

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -groundStickSpeed;
            if (groundUsesSmoothRamp && GroundNormal.y > .1f)
            {
                // On an opted-in ramp, follow its downhill drop instead of
                // outrunning gravity and repeatedly becoming airborne.
                float surfaceDrop = Vector3.Dot(horizontalVelocity,new Vector3(GroundNormal.x,0,GroundNormal.z))/GroundNormal.y;
                verticalVelocity = -Mathf.Max(groundStickSpeed,surfaceDrop+.5f);
            }
        }

        if (controller.isGrounded && command.JumpPressed)
            verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);

        verticalVelocity += gravity * deltaTime;
        Vector3 before = transform.position;
        CollisionFlags flags = controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
        ActualVelocity = (transform.position - before) / deltaTime;

        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
        if ((flags & CollisionFlags.Below) != 0 && verticalVelocity < 0f)
            verticalVelocity = -groundStickSpeed;

        GroundNormal = controller.isGrounded ? ProbeGroundNormal() : Vector3.up;
    }

    private float UphillSpeedFactor(Vector3 direction)
    {
        float slope = Vector3.Angle(GroundNormal, Vector3.up);
        if (slope <= slowdownStartsAtDegrees)
            return 1f;

        Vector3 uphill = new Vector3(-GroundNormal.x, 0f, -GroundNormal.z);
        if (uphill.sqrMagnitude < 0.0001f)
            return 1f;

        float uphillTravel = Mathf.Max(0f, Vector3.Dot(direction, uphill.normalized));
        float slopeFraction = Mathf.InverseLerp(slowdownStartsAtDegrees, fullSlowdownAtDegrees, slope);
        slopeFraction = slopeFraction * slopeFraction * (3f - 2f * slopeFraction);
        return 1f - maximumUphillSlowdown * slopeFraction * uphillTravel;
    }

    private Vector3 ProbeGroundNormal()
    {
        float radius = controller.radius * 0.8f;
        Vector3 origin = transform.position + Vector3.up * (controller.radius + 0.25f);
        int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits,
            controller.radius + 0.55f, solidSurfaceMask, QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        Vector3 normal = Vector3.up;
        groundUsesSmoothRamp = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = groundHits[i];
            if (hit.collider == controller || hit.collider.transform.IsChildOf(transform) || hit.normal.y <= 0.1f)
                continue;

            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                normal = hit.normal;
                var smooth = hit.collider.GetComponent<SmoothWalkSurface>();
                groundUsesSmoothRamp = smooth != null && smooth.isActiveAndEnabled;
            }
        }

        return normal;
    }

    private bool CanStand()
    {
        float radius = controller.radius * 0.95f;
        float addedHeight = standingHeight - crouchingHeight;
        Vector3 center = transform.position + Vector3.up * (crouchingHeight + addedHeight * 0.5f);
        int count = Physics.OverlapBoxNonAlloc(center,
            new Vector3(radius, addedHeight * 0.5f, radius), headroomHits,
            Quaternion.identity, solidSurfaceMask, QueryTriggerInteraction.Ignore);
        if (count == headroomHits.Length)
            return false;

        for (int i = 0; i < count; i++)
        {
            Collider overlap = headroomHits[i];
            if (overlap != controller && !overlap.transform.IsChildOf(transform))
                return false;
        }

        return true;
    }

    private void SetStance(bool crouched)
    {
        IsCrouched = crouched;
        float height = crouched ? crouchingHeight : standingHeight;
        controller.height = height;
        controller.center = Vector3.up * (height * 0.5f);
        if (viewPivot != null)
            viewPivot.localPosition = Vector3.up * (crouched ? crouchingEyeHeight : standingEyeHeight);
    }
}
