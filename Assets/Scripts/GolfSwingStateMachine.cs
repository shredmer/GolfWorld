using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GolfSwingStateMachine : MonoBehaviour
{
    [TextArea(3, 10)]
    public string devNotes;

    [Header("Scene references")]
    [Tooltip("Rigid body that receives the swing force.")]
    public Rigidbody ballRigidbody;

    [Tooltip("Transform used to lock the swing direction when a swing starts. Defaults to this transform if unset.")]
    public Transform swingDirectionReference;

    [Tooltip("Camera-orbit controllers that should be disabled while the swing is active.")]
    public List<MonoBehaviour> cameraControllersToDisable = new();

    [Header("Swing thresholds")]
    [Tooltip("Absolute Y value considered close enough to the center to start tracking a new swing.")]
    [Range(0f, 0.5f)]
    public float centerDeadZone = 0.15f;

    [Tooltip("Absolute Y value considered fully deflected (top or bottom) for sticks.")]
    [Range(0.5f, 1f)]
    public float fullDeflectionThreshold = 0.95f;

    [Tooltip("How far back towards the centre the stick must move from the bottom before the follow through starts.")]
    [Range(0f, 0.5f)]
    public float followThroughStartBuffer = 0.1f;

    [Tooltip("Seconds the stick must remain near the centre to cancel a follow through that did not reach the top.")]
    [Range(0f, 1f)]
    public float followThroughCancelTime = 0.2f;

    [Header("Mouse swing distances")]
    [Tooltip("Normalized distance the mouse must travel downward to reach the backswing bottom.")]
    [Range(0.1f, 1f)]
    public float mouseBackswingDistance = 0.75f;

    [Tooltip("Normalized distance the mouse must travel upward from the bottom to finish the follow through.")]
    [Range(0.1f, 1f)]
    public float mouseFollowThroughDistance = 0.75f;

    [Tooltip("Multiplier applied to mouse delta to emulate a stick deflection.")]
    public float mouseSwingSensitivity = 0.005f;

    [Header("Swing output")]
    [Tooltip("Base impulse applied to the ball when the follow through finishes.")]
    public float swingForce = 10f;

    [Tooltip("Seconds of holding at the bottom that map to full swing force.")]
    public float maxHoldTimeForFullPower = 2f;

    [Header("Ball motion gating")]
    [Tooltip("Velocity magnitude considered to be at rest.")]
    public float stationaryVelocityThreshold = 0.05f;

    [Tooltip("Seconds the ball must remain below the rest threshold before swings are re-enabled.")]
    public float stationaryTimeRequired = 0.5f;

    [Header("Power bar UI")]
    [Tooltip("Whether to show the power bar while holding at the bottom of the swing.")]
    public bool showPowerBar = true;

    [Tooltip("Maximum width of the power bar when at full power.")]
    public float powerBarMaxWidth = 200f;

    [Tooltip("Height of the power bar.")]
    public float powerBarHeight = 12f;

    [Tooltip("Vertical offset in screen space from the ball position to place the power bar.")]
    public float powerBarVerticalOffset = 30f;

    [Tooltip("Color used to draw the power bar.")]
    public Color powerBarColor = Color.green;

    [Header("Debug display")]
    [Tooltip("When enabled, overlays debug information about swing state and output.")]
    public bool showDebugInfo = true;

    [Tooltip("Screen position for the debug label.")]
    public Vector2 debugLabelPosition = new Vector2(10f, 10f);

    private enum ShotState
    {
        PreShot,
        ActiveShot,
        AfterShot
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

    private ShotState shotState = ShotState.PreShot;
    private SwingInputSource swingInputSource = SwingInputSource.None;
    private SwingPhase swingPhase = SwingPhase.WaitingForStart;

    private float backswingStartTime;
    private bool readyForBackswing;
    private float bottomReachedTime;
    private float followThroughStartTime;
    private float holdDuration;
    private float previousStickY;
    private float followThroughCancelTimer;
    private float stationaryTimer;

    private Vector2 mouseSwingValue;
    private Vector3 lockedDirection = Vector3.forward;

    private void Update()
    {
        if (shotState == ShotState.AfterShot)
        {
            UpdateAfterShot();
            return;
        }

        if (shotState == ShotState.PreShot && TryBeginActiveShot())
        {
            EnterActiveShot();
        }

        if (shotState != ShotState.ActiveShot)
        {
            return;
        }

        if (!IsSwingHeld())
        {
            CancelActiveShot();
            return;
        }

        Vector2 swingInput = ReadSwingInput();

        UpdateSwingState(swingInput);

        previousStickY = swingInput.y;
    }

    private void UpdateAfterShot()
    {
        if (ballRigidbody == null)
        {
            shotState = ShotState.PreShot;
            return;
        }

        bool stationary = ballRigidbody.linearVelocity.sqrMagnitude <= stationaryVelocityThreshold * stationaryVelocityThreshold;
        stationaryTimer = stationary ? stationaryTimer + Time.deltaTime : 0f;

        if (stationaryTimer >= stationaryTimeRequired)
        {
            shotState = ShotState.PreShot;
            stationaryTimer = 0f;
            SetCameraControllersEnabled(true);
        }
    }

    private bool TryBeginActiveShot()
    {
        bool gamepadHeld = Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame;
        bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;

        if (gamepadHeld)
        {
            swingInputSource = SwingInputSource.Gamepad;
        }
        else if (mouseHeld)
        {
            swingInputSource = SwingInputSource.Mouse;
        }
        else
        {
            swingInputSource = SwingInputSource.None;
            return false;
        }

        lockedDirection = GetCurrentAimDirection();
        return true;
    }

    private void EnterActiveShot()
    {
        shotState = ShotState.ActiveShot;
        swingPhase = SwingPhase.WaitingForStart;
        ResetSwingTracking();
        SetCameraControllersEnabled(false);
    }

    private void CancelActiveShot()
    {
        shotState = ShotState.PreShot;
        swingInputSource = SwingInputSource.None;
        ResetSwingTracking();
        SetCameraControllersEnabled(true);
    }

    private bool IsSwingHeld()
    {
        return swingInputSource switch
        {
            SwingInputSource.Gamepad => Gamepad.current != null && Gamepad.current.leftShoulder.isPressed,
            SwingInputSource.Mouse => Mouse.current != null && Mouse.current.leftButton.isPressed,
            _ => false,
        };
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

    private void UpdateSwingState(Vector2 input)
    {
        switch (swingPhase)
        {
            case SwingPhase.WaitingForStart:
                HandleWaitingForStart(input);
                if (readyForBackswing && input.y < -GetBackswingThreshold())
                {
                    swingPhase = SwingPhase.Backswing;
                    backswingStartTime = Time.time;
                    readyForBackswing = false;
                }

                break;
            case SwingPhase.Backswing:
                HandleBackswing(input);
                break;
            case SwingPhase.HoldingAtBottom:
                HandleHoldAtBottom(input);
                break;
            case SwingPhase.FollowThrough:
                HandleFollowThrough(input);
                break;
            case SwingPhase.WaitingForReset:
                HandleWaitingForReset(input);
                break;
        }
    }

    private void HandleWaitingForStart(Vector2 input)
    {
        if (Mathf.Abs(input.y) <= centerDeadZone)
        {
            readyForBackswing = true;
        }
    }

    private void HandleBackswing(Vector2 input)
    {
        if (input.y <= -GetBackswingThreshold())
        {
            bottomReachedTime = Time.time;
            swingPhase = SwingPhase.HoldingAtBottom;
            return;
        }

        if (input.y > -centerDeadZone)
        {
            swingPhase = SwingPhase.WaitingForStart;
        }
    }

    private void HandleHoldAtBottom(Vector2 input)
    {
        float followThroughTrigger = -GetBackswingThreshold() + followThroughStartBuffer;
        if (input.y >= followThroughTrigger && input.y > previousStickY)
        {
            followThroughStartTime = Time.time;
            holdDuration = followThroughStartTime - bottomReachedTime;
            swingPhase = SwingPhase.FollowThrough;
            followThroughCancelTimer = 0f;
        }
    }

    private void HandleFollowThrough(Vector2 input)
    {
        bool nearCentre = input.sqrMagnitude <= centerDeadZone * centerDeadZone;
        if (nearCentre)
        {
            followThroughCancelTimer += Time.deltaTime;
            if (followThroughCancelTimer >= followThroughCancelTime)
            {
                swingPhase = SwingPhase.WaitingForStart;
                return;
            }
        }
        else
        {
            followThroughCancelTimer = 0f;
        }

        if (input.y >= GetFollowThroughThreshold())
        {
            CompleteSwing();
            return;
        }

        float followThroughTrigger = -GetBackswingThreshold() + followThroughStartBuffer;
        bool movingDownward = input.y < previousStickY;
        bool returnedTowardsBottom = input.y <= followThroughTrigger;
        if (movingDownward && returnedTowardsBottom)
        {
            swingPhase = SwingPhase.WaitingForStart;
            followThroughCancelTimer = 0f;
        }
    }

    private void HandleWaitingForReset(Vector2 input)
    {
        if (Mathf.Abs(input.y) <= centerDeadZone)
        {
            readyForBackswing = true;
            swingPhase = SwingPhase.WaitingForStart;
        }
    }

    private float GetBackswingThreshold()
    {
        return swingInputSource == SwingInputSource.Mouse ? mouseBackswingDistance : fullDeflectionThreshold;
    }

    private float GetFollowThroughThreshold()
    {
        return swingInputSource == SwingInputSource.Mouse ? mouseFollowThroughDistance : fullDeflectionThreshold;
    }

    private void CompleteSwing()
    {
        ApplySwingForce();
        swingPhase = SwingPhase.WaitingForReset;
        swingInputSource = SwingInputSource.None;
        shotState = ShotState.AfterShot;
        stationaryTimer = 0f;
        ResetSwingTracking();
    }

    private void ResetSwingTracking()
    {
        backswingStartTime = 0f;
        readyForBackswing = false;
        bottomReachedTime = 0f;
        followThroughStartTime = 0f;
        holdDuration = 0f;
        previousStickY = 0f;
        followThroughCancelTimer = 0f;
        mouseSwingValue = Vector2.zero;
    }

    private void SetCameraControllersEnabled(bool enabled)
    {
        foreach (var controller in cameraControllersToDisable)
        {
            if (controller != null)
            {
                controller.enabled = enabled;
            }
        }
    }

    private void ApplySwingForce()
    {
        if (ballRigidbody == null)
        {
            return;
        }

        float clampedHold = Mathf.Clamp(holdDuration, 0f, maxHoldTimeForFullPower);
        float powerScale = maxHoldTimeForFullPower > Mathf.Epsilon ? clampedHold / maxHoldTimeForFullPower : 1f;

        Vector3 direction = lockedDirection;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = GetCurrentAimDirection();
        }

        Vector3 impulse = direction.normalized * swingForce * powerScale;
        ballRigidbody.AddForce(impulse, ForceMode.Impulse);
    }

    private float GetCurrentPowerScale()
    {
        float currentHold = holdDuration;

        if (swingPhase == SwingPhase.HoldingAtBottom)
        {
            currentHold = Time.time - bottomReachedTime;
        }

        float clampedHold = Mathf.Clamp(currentHold, 0f, maxHoldTimeForFullPower);
        return maxHoldTimeForFullPower > Mathf.Epsilon ? clampedHold / maxHoldTimeForFullPower : 1f;
    }

    private Vector3 GetCurrentAimDirection()
    {
        Transform reference = swingDirectionReference == null ? transform : swingDirectionReference;
        Vector3 direction = reference.forward;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.forward;
        }

        return direction.normalized;
    }

    private float GetBallSpeed()
    {
        return ballRigidbody == null ? 0f : ballRigidbody.linearVelocity.magnitude;
    }

    private void OnGUI()
    {

        DrawPowerBar();

        if (!showDebugInfo)
        {
            return;
        }

        string stateText = $"State: {shotState} (Phase: {swingPhase})";
        float powerScale = GetCurrentPowerScale();
        float appliedForce = swingForce * powerScale;

        string powerText = $"Power Scale: {powerScale:F2} (Force: {appliedForce:F2})";
        string directionText = $"Direction: {lockedDirection.normalized}";
        string ballSpeedText = $"Ball Speed: {GetBallSpeed():F2}";

        GUIStyle style = new GUIStyle(GUI.skin.label);
        int baseFontSize = style.fontSize <= 0 ? 14 : style.fontSize;
        style.fontSize = Mathf.RoundToInt(baseFontSize * 3f);

        GUIContent debugContent = new GUIContent($"{stateText}\n{powerText}\n{directionText}\n{ballSpeedText}");
        float labelWidth = 640f;
        float labelHeight = style.CalcHeight(debugContent, labelWidth);

        GUI.Label(new Rect(debugLabelPosition.x, debugLabelPosition.y, labelWidth, labelHeight), debugContent, style);
    }

    private void DrawPowerBar()
    {
        if (!showPowerBar || ballRigidbody == null)
        {
            return;
        }

        if (shotState != ShotState.ActiveShot || swingPhase != SwingPhase.HoldingAtBottom)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        Vector3 screenPosition = mainCamera.WorldToScreenPoint(ballRigidbody.position);
        if (screenPosition.z < 0f)
        {
            return;
        }

        float powerScale = Mathf.Clamp01(GetCurrentPowerScale());
        float currentWidth = powerBarMaxWidth * powerScale;

        float centerX = screenPosition.x;
        float xPosition = centerX - currentWidth * 0.5f;
        float yPosition = Screen.height - screenPosition.y + powerBarVerticalOffset;

        Rect barRect = new Rect(xPosition, yPosition, currentWidth, powerBarHeight);

        Color previousColor = GUI.color;
        GUI.color = powerBarColor;
        GUI.DrawTexture(barRect, Texture2D.whiteTexture);
        GUI.color = previousColor;
    }
}