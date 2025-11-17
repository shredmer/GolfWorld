using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

public class RightThumbstickAction : MonoBehaviour
{
    [Header("Thumbstick thresholds")]
    [Tooltip("Absolute Y value considered close enough to the center to start tracking a new swing.")]
    [Range(0f, 0.5f)]
    public float centerDeadZone = 0.15f;

    [Tooltip("Absolute Y value considered fully deflected (top or bottom).")]
    [Range(0.5f, 1f)]
    public float fullDeflectionThreshold = 0.95f;

    [Tooltip("How far back towards the centre the stick must move from the bottom before the follow through starts.")]
    [Range(0f, 0.5f)]
    public float followThroughStartBuffer = 0.1f;

    [Tooltip("Seconds the stick must remain near the centre to cancel a follow through that did not reach the top.")]
    [Range(0f, 1f)]
    public float followThroughCancelTime = 0.2f;

    // SWING ACTION VARS

    [Header("Swing output")]
    [Tooltip("Rigid body that receives the swing force.")]
    public Rigidbody ballRigidbody;

    [Tooltip("Transform used to aim the swing force. Defaults to this transform.")]
    public Transform swingDirectionReference;

    [Tooltip("Base impulse applied to the ball when the follow through finishes.")]
    public float swingForce = 10f;

    [Tooltip("Seconds of holding at the bottom that map to full swing force.")]
    public float maxHoldTimeForFullPower = 2f;

    [Header("Mouse swing settings")]
    [Tooltip("Multiplier applied to mouse delta to emulate a stick deflection.")]
    public float mouseSwingSensitivity = 0.005f;

    private enum PlayerState
    {
        Orbiting,
        Swinging
    }

    private enum SwingInputSource
    {
        None,
        Gamepad,
        Mouse
    }

    private enum SwingPhase
    {
        WaitingForStart,
        Backswing,
        HoldingAtBottom,
        FollowThrough,
        WaitingForReset
    }

    private PlayerState currentState = PlayerState.Orbiting;
    private PlayerState previousState = PlayerState.Orbiting;

    private SwingInputSource swingInputSource = SwingInputSource.None;

    private SwingPhase phase = SwingPhase.WaitingForStart;
    private bool readyForBackswing;

    private float backswingStartTime;
    private float backswingAngle;

    private float bottomReachedTime;

    private float followThroughStartTime;
    private float followThroughAngle;

    private float holdDuration;

    private float previousStickY;
    private float followThroughCancelTimer;

    private Vector2 mouseSwingValue;

    private void Awake()
    {
        if (swingDirectionReference == null)
        {
            swingDirectionReference = transform;
        }
    }

    private void Update()
    {
        HandleStateTransitions();

        if (currentState != PlayerState.Swinging)
        {
            return;
        }

        Vector2 swingInput = ReadSwingInput();
        float angle = CalculateAngle(swingInput);

        UpdateState(swingInput, angle);

        previousStickY = swingInput.y;
    }

    private void HandleStateTransitions()
    {
        if (currentState != PlayerState.Swinging && IsSwingStartPressed(out SwingInputSource detectedSource))
        {
            previousState = currentState;
            currentState = PlayerState.Swinging;
            swingInputSource = detectedSource;
            ResetSwingTracking();
        }

        if (currentState == PlayerState.Swinging && IsSwingCancelPressed())
        {
            currentState = previousState;
            swingInputSource = SwingInputSource.None;
            ResetSwingTracking();
        }
    }

    private bool IsSwingStartPressed(out SwingInputSource detectedSource)
    {
        detectedSource = SwingInputSource.None;

        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
        {
            detectedSource = SwingInputSource.Gamepad;
            return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            detectedSource = SwingInputSource.Mouse;
            return true;
        }

        return false;
    }

    private bool IsSwingCancelPressed()
    {
        bool gamepadCancel = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;
        bool mouseCancel = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        return gamepadCancel || mouseCancel;
    }

    private Vector2 ReadSwingInput()
    {
        switch (swingInputSource)
        {
            case SwingInputSource.Gamepad:
                return Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;
            case SwingInputSource.Mouse:
                return ReadMouseSwing();
            default:
                return Vector2.zero;
        }
    }

    private Vector2 ReadMouseSwing()
    {
        if (Mouse.current == null)
        {
            mouseSwingValue = Vector2.zero;
            return mouseSwingValue;
        }

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();
        mouseSwingValue += mouseDelta * mouseSwingSensitivity;
        mouseSwingValue = Vector2.ClampMagnitude(mouseSwingValue, 1f);
        return mouseSwingValue;
    }

    private void ResetSwingTracking()
    {
        phase = SwingPhase.WaitingForStart;
        readyForBackswing = false;
        backswingStartTime = 0f;
        backswingAngle = 0f;
        bottomReachedTime = 0f;
        followThroughStartTime = 0f;
        followThroughAngle = 0f;
        holdDuration = 0f;
        previousStickY = 0f;
        followThroughCancelTimer = 0f;
        mouseSwingValue = Vector2.zero;
    }

    private void UpdateState(Vector2 rightStick, float angle)
    {
        switch (phase)
        {
            case SwingPhase.WaitingForStart:
                HandleWaitingForStart(rightStick);
                if (readyForBackswing && rightStick.y < -centerDeadZone)
                {
                    phase = SwingPhase.Backswing;
                    backswingStartTime = Time.time;
                    readyForBackswing = false;
                }

                break;
            case SwingPhase.Backswing:
                HandleBackswing(rightStick, angle);
                break;
            case SwingPhase.HoldingAtBottom:
                HandleHoldAtBottom(rightStick);
                break;
            case SwingPhase.FollowThrough:
                HandleFollowThrough(rightStick, angle);
                break;
            case SwingPhase.WaitingForReset:
                HandleWaitingForReset(rightStick);
                break;
        }
    }

    private void HandleWaitingForStart(Vector2 rightStick)
    {
        if (Mathf.Abs(rightStick.y) <= centerDeadZone)
        {
            readyForBackswing = true;
        }
    }

    private void HandleBackswing(Vector2 rightStick, float angle)
    {
        if (rightStick.y <= -fullDeflectionThreshold)
        {
            float duration = Time.time - backswingStartTime;
            backswingAngle = angle;
            bottomReachedTime = Time.time;
            string formattedDuration = duration.ToString("F4", CultureInfo.InvariantCulture);
            string formattedAngle = backswingAngle.ToString("F8", CultureInfo.InvariantCulture);
            Debug.Log($"Backswing completed in {formattedDuration} seconds at angle {formattedAngle} degrees.");

            phase = SwingPhase.HoldingAtBottom;
            return;
        }

        if (rightStick.y > -centerDeadZone)
        {
            phase = SwingPhase.WaitingForStart;
        }
    }

    private void HandleHoldAtBottom(Vector2 rightStick)
    {
        float followThroughTrigger = -fullDeflectionThreshold + followThroughStartBuffer;
        if (rightStick.y >= followThroughTrigger && rightStick.y > previousStickY)
        {
            followThroughStartTime = Time.time;
            holdDuration = followThroughStartTime - bottomReachedTime;
            phase = SwingPhase.FollowThrough;
            followThroughCancelTimer = 0f;
        }
    }

    private void HandleFollowThrough(Vector2 rightStick, float angle)
    {
        bool nearCentre = rightStick.sqrMagnitude <= centerDeadZone * centerDeadZone;
        if (nearCentre)
        {
            followThroughCancelTimer += Time.deltaTime;
            if (followThroughCancelTimer >= followThroughCancelTime)
            {
                phase = SwingPhase.WaitingForStart;
                return;
            }
        }
        else
        {
            followThroughCancelTimer = 0f;
        }

        if (rightStick.y >= fullDeflectionThreshold)
        {
            float duration = Time.time - followThroughStartTime;
            followThroughAngle = angle;
            string formattedDuration = duration.ToString("F4", CultureInfo.InvariantCulture);
            string formattedAngle = followThroughAngle.ToString("F8", CultureInfo.InvariantCulture);
            Debug.Log($"Follow through swing completed in {formattedDuration} seconds at angle {formattedAngle} degrees.");

            string formattedHold = holdDuration.ToString("F4", CultureInfo.InvariantCulture);
            Debug.Log($"Backswing hold duration: {formattedHold} seconds.");

            float swingPath = Mathf.DeltaAngle(backswingAngle, followThroughAngle);
            string formattedPath = swingPath.ToString("F5", CultureInfo.InvariantCulture);
            Debug.Log($"Swing path: {formattedPath} degrees.");

            ApplySwingForce();

            phase = SwingPhase.WaitingForReset;
            followThroughCancelTimer = 0f;
        }
        else
        {
            float followThroughTrigger = -fullDeflectionThreshold + followThroughStartBuffer;
            bool movingDownward = rightStick.y < previousStickY;
            bool returnedTowardsBottom = rightStick.y <= followThroughTrigger;
            if (movingDownward && returnedTowardsBottom)
            {
                phase = SwingPhase.WaitingForStart;
                followThroughCancelTimer = 0f;
            }
        }
    }

    private void HandleWaitingForReset(Vector2 rightStick)
    {
        if (Mathf.Abs(rightStick.y) <= centerDeadZone)
        {
            readyForBackswing = true;
            phase = SwingPhase.WaitingForStart;
        }
    }

    private static float CalculateAngle(Vector2 stick)
    {
        if (stick.sqrMagnitude < Mathf.Epsilon)
        {
            return 0f;
        }

        float angle = Mathf.Atan2(stick.y, stick.x) * Mathf.Rad2Deg;
        return (angle + 360f) % 360f;
    }
    private void ApplySwingForce()
    {
        if (ballRigidbody == null)
        {
            return;
        }

        float clampedHold = Mathf.Clamp(holdDuration, 0f, maxHoldTimeForFullPower);
        float powerScale = maxHoldTimeForFullPower > Mathf.Epsilon ? clampedHold / maxHoldTimeForFullPower : 1f;

        Transform reference = swingDirectionReference == null ? transform : swingDirectionReference;
        Vector3 direction = reference.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.forward;
        }

        Vector3 impulse = direction.normalized * swingForce * powerScale;
        ballRigidbody.AddForce(impulse, ForceMode.Impulse);
    }
}