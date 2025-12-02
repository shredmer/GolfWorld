using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class GolfSwingSimple : MonoBehaviour
{
    [Header("References")]
    public Camera playerCamera;
    public Transform ballTransform;
    public Rigidbody ballRigidbody;

    [Header("Look Settings")]
    public float mouseLookSensitivity = 1.5f;
    public float controllerLookSensitivity = 90f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Zoom Settings")]
    public float zoomSpeed = 4f;
    public float minDistance = 2f;
    public float maxDistance = 12f;
    public float cameraHeightOffset = 0.5f;

    [Header("Power Settings")]
    public float maxChargeTime = 2f;
    public float maxForce = 25f;
    public float barWidth = 1.6f;
    public float barHeight = 0.15f;
    public float barVerticalOffset = 0.35f;
    public Color barBackgroundColor = new Color(0f, 0f, 0f, 0.35f);
    public Color barFillColor = new Color(0.26f, 0.82f, 0.37f, 0.9f);

    [Header("Debug")]
    public bool showDebugInfo = true;
    public Vector2 debugTextAnchor = new Vector2(16f, -16f);
    public int debugFontSize = 18;

    private RectTransform barRoot;
    private Image barFill;
    private float yaw;
    private float pitch;
    private float currentDistance;
    private bool isCharging;
    private float chargeStartTime;
    private Text debugText;

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = Camera.main;
        }

        if (ballTransform == null && ballRigidbody != null)
        {
            ballTransform = ballRigidbody.transform;
        }

        currentDistance = Mathf.Clamp(Vector3.Distance(playerCamera.transform.position, GetBallPosition()), minDistance, maxDistance);
        Vector3 forward = playerCamera.transform.forward;
        yaw = playerCamera.transform.rotation.eulerAngles.y;
        pitch = Mathf.Asin(forward.y) * Mathf.Rad2Deg;

        CreatePowerBar();
        SetBarVisible(false);
        CreateDebugDisplay();
    }

    private void Update()
    {
        if (ballTransform == null || playerCamera == null)
        {
            return;
        }

        UpdateCameraLook();
        UpdateZoom();
        UpdatePower();
        UpdateDebugInfo();
    }

    private void LateUpdate()
    {
        if (ballTransform == null || playerCamera == null)
        {
            return;
        }

        UpdateCameraPosition();
        UpdateBarPosition();
    }

    private void UpdateCameraLook()
    {
        Vector2 mouseDelta = Vector2.zero;
        if (Mouse.current != null && Mouse.current.delta.IsActuated())
        {
            mouseDelta = Mouse.current.delta.ReadValue() * mouseLookSensitivity;
        }

        Vector2 stickDelta = Vector2.zero;
        if (Gamepad.current != null)
        {
            stickDelta = Gamepad.current.rightStick.ReadValue() * controllerLookSensitivity * Time.deltaTime;
        }

        yaw += mouseDelta.x + stickDelta.x;
        pitch -= mouseDelta.y + stickDelta.y;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    private void UpdateZoom()
    {
        float zoomInput = 0f;
        if (Gamepad.current != null)
        {
            zoomInput = Gamepad.current.leftStick.ReadValue().y;
        }

        currentDistance = Mathf.Clamp(currentDistance - zoomInput * zoomSpeed * Time.deltaTime, minDistance, maxDistance);
    }

    private void UpdateCameraPosition()
    {
        Vector3 ballPosition = GetBallPosition();
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 offsetDirection = rotation * Vector3.forward;
        Vector3 cameraOffset = -offsetDirection * currentDistance;
        cameraOffset.y += cameraHeightOffset;

        playerCamera.transform.position = ballPosition + cameraOffset;
        playerCamera.transform.rotation = rotation;
    }

    private void UpdatePower()
    {
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool triggerPressed = Gamepad.current != null && Gamepad.current.rightTrigger.ReadValue() > 0.01f;
        bool applyingPower = mousePressed || triggerPressed;

        if (applyingPower && !isCharging)
        {
            isCharging = true;
            chargeStartTime = Time.time;
            SetBarVisible(true);
        }
        else if (!applyingPower && isCharging)
        {
            float heldTime = Time.time - chargeStartTime;
            ApplyForce(heldTime);
            isCharging = false;
            SetBarVisible(false);
        }

        if (isCharging)
        {
            float heldTime = Time.time - chargeStartTime;
            float normalized = Mathf.Clamp01(heldTime / maxChargeTime);
            UpdateBarFill(normalized);
        }
        else
        {
            UpdateBarFill(0f);
        }
    }

    private void ApplyForce(float heldTime)
    {
        if (ballRigidbody == null)
        {
            return;
        }

        float normalized = Mathf.Clamp01(heldTime / maxChargeTime);
        float forceMagnitude = normalized * maxForce;
        Vector3 direction = playerCamera.transform.forward;
        ballRigidbody.AddForce(direction * forceMagnitude, ForceMode.Impulse);
    }

    private void CreatePowerBar()
    {
        GameObject canvasObject = new GameObject(
            "PowerBarCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        barRoot = canvasObject.GetComponent<RectTransform>();
        barRoot.sizeDelta = new Vector2(barWidth, barHeight);
        barRoot.localScale = Vector3.one;
        barRoot.pivot = new Vector2(0.5f, 0.5f);

        GameObject backgroundObject = new GameObject("PowerBarBackground");
        backgroundObject.transform.SetParent(barRoot, false);
        Image backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.color = barBackgroundColor;
        RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;

        GameObject fillObject = new GameObject("PowerBarFill");
        fillObject.transform.SetParent(backgroundRect, false);
        barFill = fillObject.AddComponent<Image>();
        barFill.color = barFillColor;
        RectTransform fillRect = fillObject.GetComponent<RectTransform>();
        fillRect.anchorMin = new Vector2(0f, 0f);
        fillRect.anchorMax = new Vector2(0f, 1f);
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        fillRect.pivot = new Vector2(0f, 0.5f);
    }

    private void CreateDebugDisplay()
    {
        if (!showDebugInfo)
        {
            return;
        }

        GameObject canvasObject = new GameObject(
            "SwingDebugCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        GameObject textObject = new GameObject("SwingDebugText");
        textObject.transform.SetParent(canvasObject.transform, false);
        debugText = textObject.AddComponent<Text>();
        debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        debugText.alignment = TextAnchor.UpperLeft;
        debugText.fontSize = debugFontSize;
        debugText.color = Color.white;

        RectTransform textRect = debugText.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = debugTextAnchor;
    }

    private void UpdateBarPosition()
    {
        if (barRoot == null || ballTransform == null)
        {
            return;
        }

        Vector3 ballPosition = GetBallPosition();
        Vector3 worldPosition = ballPosition - Vector3.up * barVerticalOffset;
        barRoot.position = worldPosition;
        barRoot.rotation = Quaternion.LookRotation(playerCamera.transform.forward, playerCamera.transform.up);
    }

    private void UpdateBarFill(float normalized)
    {
        if (barFill == null)
        {
            return;
        }

        normalized = Mathf.Clamp01(normalized);
        RectTransform fillRect = barFill.rectTransform;
        fillRect.anchorMax = new Vector2(normalized, 1f);
    }

    private void SetBarVisible(bool visible)
    {
        if (barRoot != null)
        {
            barRoot.gameObject.SetActive(visible);
        }
    }

    private void UpdateDebugInfo()
    {
        if (!showDebugInfo || debugText == null)
        {
            return;
        }

        float speed = ballRigidbody != null ? ballRigidbody.linearVelocity.magnitude : 0f;
        Vector3 cameraForward = playerCamera != null ? playerCamera.transform.forward : Vector3.forward;
        Vector3 launchDirection = cameraForward.normalized;

        float cameraPitch = Mathf.Asin(Mathf.Clamp(cameraForward.y, -1f, 1f)) * Mathf.Rad2Deg;
        float launchAngle = Mathf.Asin(Mathf.Clamp(launchDirection.y, -1f, 1f)) * Mathf.Rad2Deg;

        debugText.text =
            $"Ball Speed: {speed:F2} m/s\n" +
            $"Camera Pitch: {cameraPitch:F1}�\n" +
            $"Launch Angle: {launchAngle:F1}�";
    }

    private Vector3 GetBallPosition()
    {
        if (ballTransform != null)
        {
            return ballTransform.position;
        }

        return Vector3.zero;
    }
}