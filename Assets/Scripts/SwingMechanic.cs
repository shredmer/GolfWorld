using UnityEngine;
using UnityEngine.InputSystem;

public class SwingMechanic : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private Camera targetCamera;

    [Header("Launch Angle Settings")]
    [SerializeField] private float defaultLaunchAngle = 15f;
    [SerializeField] private float minLaunchAngle = 0f;
    [SerializeField] private float maxLaunchAngle = 60f;
    [SerializeField] private float stickAngleSpeed = 45f;
    [SerializeField] private float mouseAngleSpeed = 0.25f;

    [Header("Power Settings")]
    [SerializeField] private float powerBuildRate = 12f;
    [SerializeField] private float maxPower = 25f;
    [SerializeField] private float minimumHoldTime = 0.5f;
    [SerializeField] private ForceMode forceMode = ForceMode.Impulse;

    [Header("Debug Display")]
    [SerializeField] private Vector2 debugTextPosition = new Vector2(16f, 16f);

    private float currentLaunchAngle;
    private float currentPower;
    private float holdTime;
    private bool isCharging;

    private void Reset()
    {
        targetRigidbody = GetComponent<Rigidbody>();
        if (Camera.main != null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Awake()
    {
        currentLaunchAngle = Mathf.Clamp(defaultLaunchAngle, minLaunchAngle, maxLaunchAngle);
    }

    private void Update()
    {
        UpdateLaunchAngle();
        UpdatePowerCharge();
    }

    private void UpdateLaunchAngle()
    {
        bool isAdjusting = IsAngleAdjustPressed();
        if (!isAdjusting)
        {
            return;
        }

        float angleDelta = 0f;
        if (Gamepad.current != null)
        {
            float stickY = Gamepad.current.leftStick.ReadValue().y;
            angleDelta += stickY * stickAngleSpeed * Time.deltaTime;
        }

        if (Mouse.current != null)
        {
            float mouseY = Mouse.current.delta.ReadValue().y;
            angleDelta += mouseY * mouseAngleSpeed;
        }

        if (Mathf.Abs(angleDelta) > Mathf.Epsilon)
        {
            currentLaunchAngle = Mathf.Clamp(currentLaunchAngle + angleDelta, minLaunchAngle, maxLaunchAngle);
        }
    }

    private void UpdatePowerCharge()
    {
        bool isPressed = IsPowerPressed();

        if (isPressed)
        {
            if (!isCharging)
            {
                isCharging = true;
                holdTime = 0f;
                currentPower = 0f;
            }

            holdTime += Time.deltaTime;
            currentPower = Mathf.Min(maxPower, currentPower + powerBuildRate * Time.deltaTime);
        }
        else if (isCharging)
        {
            ReleaseIfReady();
            isCharging = false;
            holdTime = 0f;
            currentPower = 0f;
        }
    }

    private bool IsAngleAdjustPressed()
    {
        bool triggerPressed = false;
        if (Gamepad.current != null)
        {
            triggerPressed = Gamepad.current.leftTrigger.ReadValue() > 0.1f;
        }

        bool mousePressed = false;
        if (Mouse.current != null)
        {
            mousePressed = Mouse.current.rightButton.isPressed;
        }

        return triggerPressed || mousePressed;
    }

    private bool IsPowerPressed()
    {
        bool triggerPressed = false;
        if (Gamepad.current != null)
        {
            triggerPressed = Gamepad.current.rightTrigger.ReadValue() > 0.1f;
        }

        bool mousePressed = false;
        if (Mouse.current != null)
        {
            mousePressed = Mouse.current.leftButton.isPressed;
        }

        return triggerPressed || mousePressed;
    }

    private void ReleaseIfReady()
    {
        if (holdTime < minimumHoldTime)
        {
            return;
        }

        if (targetRigidbody == null || targetCamera == null)
        {
            return;
        }

        Vector3 flatForward = Vector3.ProjectOnPlane(targetCamera.transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        Vector3 rightAxis = Vector3.Cross(Vector3.up, flatForward).normalized;
        Vector3 launchDirection = Quaternion.AngleAxis(-currentLaunchAngle, rightAxis) * flatForward.normalized;
        if (launchDirection.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        targetRigidbody.AddForce(launchDirection.normalized * currentPower, forceMode);
    }

    private void OnGUI()
    {
        float cameraYaw = 0f;
        if (targetCamera != null)
        {
            Vector3 forward = targetCamera.transform.forward;
            cameraYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            cameraYaw = (cameraYaw + 360f) % 360f;
        }

        string powerLabel = $"Power: {currentPower:F2}";
        string holdLabel = $"Hold Time: {holdTime:F2}s";
        string yawLabel = $"Camera Yaw: {cameraYaw:F1}°";
        string angleLabel = $"Launch Angle: {currentLaunchAngle:F1}°";

        Rect powerRect = new Rect(debugTextPosition.x, debugTextPosition.y, 280f, 20f);
        Rect holdRect = new Rect(debugTextPosition.x, debugTextPosition.y + 20f, 280f, 20f);
        Rect yawRect = new Rect(debugTextPosition.x, debugTextPosition.y + 40f, 280f, 20f);
        Rect angleRect = new Rect(debugTextPosition.x, debugTextPosition.y + 60f, 280f, 20f);

        GUI.Label(powerRect, powerLabel);
        GUI.Label(holdRect, holdLabel);
        GUI.Label(yawRect, yawLabel);
        GUI.Label(angleRect, angleLabel);
    }
}