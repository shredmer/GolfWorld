using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;

public class RightThumbstickLogger : MonoBehaviour
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

    private enum SwingPhase
    {
        WaitingForStart,
        Backswing,
        HoldingAtBottom,
        FollowThrough,
        WaitingForReset
    }

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

    private void Update()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null)
        {
            return;
        }

        Vector2 rightStick = gamepad.rightStick.ReadValue();
        float angle = CalculateAngle(rightStick);

        UpdateState(rightStick, angle);

        previousStickY = rightStick.y;
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
}