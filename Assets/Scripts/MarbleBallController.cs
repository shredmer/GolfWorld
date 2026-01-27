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
    [SerializeField] private float stopSpeedThreshold = 0.05f;
    [SerializeField] private float stopAngularDamping = 12f;
    [SerializeField] private RigidbodyInterpolation interpolationMode = RigidbodyInterpolation.Interpolate;

    [Header("Jump")]
    [SerializeField] private float jumpImpulse = 6.5f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBuffer = 0.12f;

    [Header("Bounce")]
    [SerializeField, Range(0f, 1f)] private float bounciness = 0.20f;
    [SerializeField] private PhysicsMaterialCombine bounceCombine = PhysicsMaterialCombine.Maximum;

    [Header("Airborne Weight")]
    [SerializeField] private float extraAirGravity = 18f;

    [Header("Grounding")]
    [SerializeField] private float groundCheckRadius = 0.45f;
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Debug")]
    [SerializeField] private bool showDebug = true;

    private Rigidbody rb;
    private Collider ballCollider; 
    private Vector2 moveInput;
    private float lastGroundedTime = -999f;
    private float lastJumpPressedTime = -999f;
    private bool jumpHeld;
    private bool isGrounded;
    private float heightOffGround;
    private float ballSpeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ballCollider = GetComponent<Collider>(); //Cached the ball collider to reuse it for bounce setup and grounding calculations, reducing redundant lookups
        rb.interpolation = interpolationMode;
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        ApplyBounceMaterial();
    }

    private void ApplyBounceMaterial()
    {
        if (ballCollider == null)
        {
            return;
        }

        PhysicsMaterial bounceMaterial = new PhysicsMaterial("BallBounce")
        {
            bounciness = bounciness,
            bounceCombine = bounceCombine,
            dynamicFriction = 0f,
            staticFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum
        };

        ballCollider.material = bounceMaterial;
    }

    private void Update()
    {
        ReadInput();
    }

    private void FixedUpdate()
    {
        isGrounded = CheckGrounded();
        if (isGrounded)
        {
            lastGroundedTime = Time.time;
        }

        ApplyMovement(isGrounded);
        ApplyAirGravity(isGrounded);
        HandleJump(isGrounded);
        ApplyAngularDamping(isGrounded);
        UpdateDebugMetrics();
    }

    private void ReadInput()
    {
        moveInput = Vector2.zero;
        jumpHeld = false;

        if (Gamepad.current != null)
        {
            moveInput = Gamepad.current.leftStick.ReadValue();
            jumpHeld = Gamepad.current.buttonSouth.isPressed;
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
            if (Keyboard.current.spaceKey.isPressed)
            {
                jumpHeld = true;
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

    private void ApplyAirGravity(bool grounded)
    {
        if (grounded)
        {
            return;
        }

        rb.AddForce(Vector3.down * extraAirGravity, ForceMode.Acceleration);
    }

    private void ApplyAngularDamping(bool grounded)
    {
        if (!grounded)
        {
            return;
        }

        if (rb.linearVelocity.sqrMagnitude > stopSpeedThreshold * stopSpeedThreshold)
        {
            return;
        }

        Vector3 angularVelocity = rb.angularVelocity;
        if (angularVelocity.sqrMagnitude > 0.0001f)
        {
            float damping = 1f - Mathf.Clamp01(stopAngularDamping * Time.fixedDeltaTime);
            rb.angularVelocity = angularVelocity * damping;
        }
    }

    private void HandleJump(bool grounded)
    {
        bool canUseCoyote = Time.time - lastGroundedTime <= coyoteTime;
        bool hasBufferedJump = Time.time - lastJumpPressedTime <= jumpBuffer;

        if ((!hasBufferedJump && !jumpHeld) || !canUseCoyote)
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

        if (ballCollider != null)
        {
            Bounds bounds = ballCollider.bounds;
            float maxProbeRadius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            radius = Mathf.Clamp(radius, 0.05f, maxProbeRadius);
            distance = Mathf.Max(distance + bounds.extents.y - radius, 0.01f);
            origin = bounds.center;
        }

        return Physics.SphereCast(origin, radius, Vector3.down, out _, distance, groundLayers,
            QueryTriggerInteraction.Ignore);
    }

	private void UpdateDebugMetrics()
    {
        ballSpeed = rb.linearVelocity.magnitude;

        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, Mathf.Infinity, groundLayers,
            QueryTriggerInteraction.Ignore))
        {
            heightOffGround = hit.distance;
        }
        else
        {
            heightOffGround = Mathf.Infinity;
        }
    }

    private void OnGUI()
    {
        if (!showDebug)
        {
            return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            normal = { textColor = Color.white }
        };

        string heightText = float.IsInfinity(heightOffGround) ? "N/A" : $"{heightOffGround:0.00} m";
        string debugText =
            $"Speed: {ballSpeed:0.00} m/s\nHeight: {heightText}\nGrounded: {isGrounded}";

        GUI.Label(new Rect(12f, 12f, 320f, 80f), debugText, style);
    }
}