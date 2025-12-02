using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GolfSwingController2 : MonoBehaviour
{
    [Header("Scene references")]
    [Tooltip("Rigid body that receives the swing force.")]
    public Rigidbody ballRigidbody;

    [Tooltip("Transform used to aim the swing force. Defaults to this transform if unset.")]
    public Transform swingDirectionReference;

    [Tooltip("Camera-orbit controllers that should be disabled while the swing is active.")]
    public List<MonoBehaviour> cameraControllersToDisable = new();

    [Tooltip("PlayerInput whose action maps should be disabled while the swing is active.")]
    public PlayerInput playerInput;

    [Tooltip("Names of action maps to disable on the provided PlayerInput while swinging.")]
    public List<string> cameraActionMapsToDisable = new();

    [Tooltip("Individual actions to disable on the provided PlayerInput while swinging (e.g. Look).")]
    public List<string> cameraActionsToDisable = new() { "Look" };

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

    [Header("Debug display")]
    [Tooltip("When enabled, overlays debug information about swing state and output.")]
    public bool showDebugInfo = true;
    [Tooltip("Screen position for the debug label.")]
    public Vector2 debugLabelPosition = new Vector2(10f, 10f);

    [Tooltip("Font size for the debug label.")]
    public int debugFontSize = 24;

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

    private readonly List<InputActionMap> disabledActionMaps = new();
    private readonly List<InputAction> disabledActions = new();

    private Vector2 mouseSwingValue;

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
        }
    }

    private bool TryBeginActiveShot()
    {
        bool gamepadHeld = Gamepad.current != null && Gamepad.current.leftShoulder.isPressed;
        bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;

        if (gamepadHeld)
        {
            swingInputSource = SwingInputSource.Gamepad;
            return true;
        }

        if (mouseHeld)
        {
            swingInputSource = SwingInputSource.Mouse;
            return true;
        }

        swingInputSource = SwingInputSource.None;
        return false;
    }

    private void EnterActiveShot()
    {
        shotState = ShotState.ActiveShot;
        swingPhase = SwingPhase.WaitingForStart;
        ResetSwingTracking();
        SetCameraControllersEnabled(false);
        SetCameraActionMapsEnabled(false);
        SetCameraActionsEnabled(false);
    }

    private void CancelActiveShot()
    {
        shotState = ShotState.PreShot;
        swingInputSource = SwingInputSource.None;
        ResetSwingTracking();
        SetCameraControllersEnabled(true);
        SetCameraActionMapsEnabled(true);
        SetCameraActionsEnabled(true);
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
        SetCameraControllersEnabled(true);
        SetCameraActionMapsEnabled(true);
        SetCameraActionsEnabled(true);
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

    private void SetCameraActionMapsEnabled(bool enabled)
    {
        if (playerInput == null)
        {
            return;
        }

        if (enabled)
        {
            foreach (var map in disabledActionMaps)
            {
                if (map != null)
                {
                    map.Enable();
                }
            }

            disabledActionMaps.Clear();
            return;
        }

        disabledActionMaps.Clear();
        foreach (string mapName in cameraActionMapsToDisable)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                continue;
            }

            InputActionMap map = playerInput.actions.FindActionMap(mapName, throwIfNotFound: false);
            if (map != null && map.enabled)
            {
                map.Disable();
                disabledActionMaps.Add(map);
            }
        }
    }

    private void SetCameraActionsEnabled(bool enabled)
    {
        if (playerInput == null)
        {
            return;
        }

        if (enabled)
        {
            foreach (var action in disabledActions)
            {
                if (action != null)
                {
                    action.Enable();
                }
            }

            disabledActions.Clear();
            return;
        }

        disabledActions.Clear();
        foreach (string actionName in cameraActionsToDisable)
        {
            if (string.IsNullOrWhiteSpace(actionName))
            {
                continue;
            }

            InputAction action = playerInput.actions.FindAction(actionName, throwIfNotFound: false);
            if (action != null && action.enabled)
            {
                action.Disable();
                disabledActions.Add(action);
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

    private void OnGUI()
    {
        if (!showDebugInfo)
        {
            return;
        }

        string stateText = $"State: {shotState} (Phase: {swingPhase})";
        float powerScale = GetCurrentPowerScale();
        float appliedForce = swingForce * powerScale;
        float ballSpeed = ballRigidbody != null ? ballRigidbody.linearVelocity.magnitude : 0f;

        Transform reference = swingDirectionReference == null ? transform : swingDirectionReference;
        Vector3 direction = reference.forward;
        direction.y = 0f;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            direction = Vector3.forward;
        }

        string powerText = $"Power Scale: {powerScale:F2} (Force: {appliedForce:F2})";
        string directionText = $"Direction: {direction.normalized}";
        string speedText = $"Ball Speed: {ballSpeed:F2}";

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = debugFontSize
        };

        GUI.Label(new Rect(debugLabelPosition.x, debugLabelPosition.y, 600f, 120f), $"{stateText}\n{powerText}\n{directionText}\n{speedText}", style);
    }
}