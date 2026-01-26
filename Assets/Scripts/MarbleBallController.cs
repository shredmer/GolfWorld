using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class MarbleBallController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform cameraTransform;

    [Header("Movement")]
    [SerializeField] private float maxSpeed = 12f;
    [SerializeField] private float groundAcceleration = 35f;
    [SerializeField] private float airAcceleration = 15f;
    [SerializeField] private float groundFriction = 6f;
    [SerializeField] private float torqueStrength = 45f;

    [Header("Jump")]
    [SerializeField] private float jumpImpulse = 6.5f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBuffer = 0.12f;

    [Header("Grounding")]
    [SerializeField] private float groundCheckRadius = 0.45f;
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private LayerMask groundLayers = ~0;

    private Rigidbody rb;
    private Vector2 moveInput;
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    private void Update()
    {
        ReadInput();
    }

    private void FixedUpdate()
    {
        bool grounded = CheckGrounded();
        if (grounded)
        {
            lastGroundedTime = Time.time;
        }

        ApplyMovement(grounded);
        HandleJump(grounded);
    }

    private void ReadInput()
    {
        moveInput = Vector2.zero;

        if (Gamepad.current != null)
        {
            moveInput = Gamepad.current.leftStick.ReadValue();
            if (Gamepad.current.buttonSouth.wasPressedThisFrame)
            {
                lastJumpPressedTime = Time.time;
            }
        }

        if (Keyboard.current != null)
        {
            Vector2 keyboardInput = Vector2.zero;
            if (Keyboard.current.wKey.isPressed)
            {
                keyboardInput.y += 1f;
            }
            if (Keyboard.current.sKey.isPressed)
            {
                keyboardInput.y -= 1f;
            }
            if (Keyboard.current.dKey.isPressed)
            {
                keyboardInput.x += 1f;
            }
            if (Keyboard.current.aKey.isPressed)
            {
                keyboardInput.x -= 1f;
            }

            if (keyboardInput.sqrMagnitude > 0f)
            {
                moveInput = Vector2.ClampMagnitude(moveInput + keyboardInput, 1f);
            }

            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                lastJumpPressedTime = Time.time;
            }
        }
    }

    private void ApplyMovement(bool grounded)
    {
        if (moveInput.sqrMagnitude < 0.001f)
        {
            if (grounded)
            {
                ApplyFriction();
            }

            return;
        }

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
            right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        }

        Vector3 moveDirection = (forward * moveInput.y + right * moveInput.x).normalized;
        float acceleration = grounded ? groundAcceleration : airAcceleration;

        Vector3 velocity = rb.linearVelocity;
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        Vector3 desiredVelocity = moveDirection * maxSpeed;
        Vector3 velocityChange = desiredVelocity - horizontalVelocity;
        Vector3 accel = Vector3.ClampMagnitude(velocityChange / Time.fixedDeltaTime, acceleration);
        rb.AddForce(accel, ForceMode.Acceleration);

        Vector3 torqueAxis = Vector3.Cross(Vector3.up, moveDirection);
        rb.AddTorque(torqueAxis * torqueStrength, ForceMode.Acceleration);
    }

    private void ApplyFriction()
    {
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 frictionForce = -horizontalVelocity * groundFriction;
        rb.AddForce(frictionForce, ForceMode.Acceleration);
    }

    private void HandleJump(bool grounded)
    {
        bool canUseCoyote = Time.time - lastGroundedTime <= coyoteTime;
        bool hasBufferedJump = Time.time - lastJumpPressedTime <= jumpBuffer;

        if (!hasBufferedJump || !canUseCoyote)
        {
            return;
        }

        Vector3 velocity = rb.linearVelocity;
        if (velocity.y < 0f)
        {
            velocity.y = 0f;
            rb.linearVelocity = velocity;
        }

        rb.AddForce(Vector3.up * jumpImpulse, ForceMode.VelocityChange);
        lastJumpPressedTime = -999f;
    }

    private bool CheckGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.1f;
        float radius = Mathf.Max(groundCheckRadius, 0.05f);
        float distance = Mathf.Max(groundCheckDistance, 0.01f);

        return Physics.SphereCast(origin, radius, Vector3.down, out _, distance, groundLayers,
            QueryTriggerInteraction.Ignore);
    }
}