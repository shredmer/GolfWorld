using UnityEngine;
using UnityEngine.InputSystem;

public class HoldRelease : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody targetRigidbody;
    [SerializeField] private Camera targetCamera;

    [Header("Power Settings")]
    [SerializeField] private float powerBuildRate = 12f;
    [SerializeField] private float maxPower = 25f;
    [SerializeField] private float minimumHoldTime = 0.5f;
    [SerializeField] private ForceMode forceMode = ForceMode.Impulse;

    [Header("Debug Display")]
    [SerializeField] private Vector2 debugTextPosition = new Vector2(16f, 16f);

    private float currentPower;
    private float holdTime;
    private bool isHolding;

    private void Reset()
    {
        targetRigidbody = GetComponent<Rigidbody>();
        if (Camera.main != null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Update()
    {
        bool isPressed = IsInputPressed();

        if (isPressed)
        {
            if (!isHolding)
            {
                isHolding = true;
                holdTime = 0f;
                currentPower = 0f;
            }

            holdTime += Time.deltaTime;
            currentPower = Mathf.Min(maxPower, currentPower + powerBuildRate * Time.deltaTime);
        }
        else if (isHolding)
        {
            ReleaseIfReady();
            isHolding = false;
            holdTime = 0f;
            currentPower = 0f;
        }
    }

    private bool IsInputPressed()
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

        Vector3 direction = targetCamera.transform.forward;
        if (direction.sqrMagnitude <= Mathf.Epsilon)
        {
            return;
        }

        targetRigidbody.AddForce(direction.normalized * currentPower, forceMode);
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
        string angleLabel = $"Camera Yaw: {cameraYaw:F1}°";

        Rect powerRect = new Rect(debugTextPosition.x, debugTextPosition.y, 280f, 20f);
        Rect holdRect = new Rect(debugTextPosition.x, debugTextPosition.y + 20f, 280f, 20f);
        Rect angleRect = new Rect(debugTextPosition.x, debugTextPosition.y + 40f, 280f, 20f);

        GUI.Label(powerRect, powerLabel);
        GUI.Label(holdRect, holdLabel);
        GUI.Label(angleRect, angleLabel);
    }
}