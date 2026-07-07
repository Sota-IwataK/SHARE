using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RoverControlStatusHUD : MonoBehaviour
{
    private static RoverControlStatusHUD instance;

    [SerializeField] private RightHandMecanumControl roverController;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private float refreshRateHz = 10f;
    [SerializeField] private bool followHead = true;
    [SerializeField] private Vector3 localOffset = new Vector3(0.32f, 0.18f, 0.7f);
    [SerializeField] private float notificationDurationSec = 1.5f;

    private const float CanvasScale = 0.0015f;
    private const float PanelWidth = 560f;
    private const float PanelHeight = 120f;

    private Transform cachedHead;
    private Canvas notificationCanvas;
    private GameObject notificationPanel;
    private Coroutine hideRoutine;
    private float nextRefreshTime;
    private bool hasPreviousState;
    private bool pendingPublishingNotification;
    private bool showingPriorityNotification;

    private bool previousHandTracked;
    private bool previousGestureHolding;
    private bool previousDriveActive;
    private bool previousPublishing;
    private string previousStateText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeHud()
    {
        var existingHud = FindFirstObjectByType<RoverControlStatusHUD>();
        if (existingHud != null || instance != null || HasAnyHudInstanceIncludingInactive())
        {
            return;
        }

        var hudObject = new GameObject("RoverControlStatusHUD");
        hudObject.AddComponent<RoverControlStatusHUD>();
    }

    private void Awake()
    {
        Debug.Log(
            $"[RoverHUD] Awake instance={GetInstanceID()} " +
            $"count={FindObjectsByType<RoverControlStatusHUD>(FindObjectsSortMode.None).Length}");

        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        ResolveControllerOnce();
        EnsureNotificationView();
        HideNotification();
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnEnable()
    {
        nextRefreshTime = 0f;
        hasPreviousState = false;
        pendingPublishingNotification = false;
        showingPriorityNotification = false;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        CheckStateTransitions();
        nextRefreshTime = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRateHz);
    }

    private void LateUpdate()
    {
        if (!followHead)
        {
            return;
        }

        Transform head = ResolveHead();
        if (head == null)
        {
            return;
        }

        transform.position =
            head.position +
            head.right * localOffset.x +
            head.up * localOffset.y +
            head.forward * localOffset.z;
        transform.rotation = Quaternion.LookRotation(transform.position - head.position, head.up);
    }

    private void ResolveControllerOnce()
    {
        if (roverController == null)
        {
            roverController = FindFirstObjectByType<RightHandMecanumControl>();
        }
    }

    private void EnsureNotificationView()
    {
        if (statusText != null)
        {
            notificationCanvas = statusText.GetComponentInParent<Canvas>(true);
            notificationPanel = statusText.transform.parent != null
                ? statusText.transform.parent.gameObject
                : statusText.gameObject;
            return;
        }

        var canvasObject = new GameObject("RoverControlStatusCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        var canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        canvasRect.localScale = Vector3.one * CanvasScale;

        notificationCanvas = canvasObject.GetComponent<Canvas>();
        notificationCanvas.renderMode = RenderMode.WorldSpace;
        notificationCanvas.worldCamera = Camera.main;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        notificationPanel = new GameObject("RoverControlStatusPanel", typeof(RectTransform), typeof(Image));
        notificationPanel.transform.SetParent(canvasObject.transform, false);

        var panelRect = notificationPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var panelImage = notificationPanel.GetComponent<Image>();
        panelImage.color = new Color(0f, 0f, 0f, 0.72f);

        var textObject = new GameObject("RoverControlStatusText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(notificationPanel.transform, false);

        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(24f, 16f);
        textRect.offsetMax = new Vector2(-24f, -16f);

        statusText = textObject.GetComponent<TextMeshProUGUI>();
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.white;
        statusText.fontSize = 34f;
        statusText.fontStyle = FontStyles.Bold;
        statusText.enableWordWrapping = false;
        statusText.raycastTarget = false;
    }

    private Transform ResolveHead()
    {
        if (cachedHead != null)
        {
            return cachedHead;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cachedHead = mainCamera.transform;
        }

        return cachedHead;
    }

    private void CheckStateTransitions()
    {
        if (roverController == null)
        {
            ResolveControllerOnce();
        }

        if (roverController == null)
        {
            return;
        }

        bool handTracked = roverController.IsHandTracked;
        bool gestureHolding = roverController.IsGestureHolding;
        bool driveActive = roverController.IsDriveActive;
        bool publishing = roverController.IsPublishing;
        string stateText = roverController.CurrentStateText;

        string message = SelectNotificationMessage(
            handTracked,
            gestureHolding,
            driveActive,
            publishing,
            stateText);

        if (!string.IsNullOrEmpty(message))
        {
            ShowNotification(message);
        }

        previousHandTracked = handTracked;
        previousGestureHolding = gestureHolding;
        previousDriveActive = driveActive;
        previousPublishing = publishing;
        previousStateText = stateText;
        hasPreviousState = true;
    }

    private string SelectNotificationMessage(
        bool handTracked,
        bool gestureHolding,
        bool driveActive,
        bool publishing,
        string stateText)
    {
        bool firstState = !hasPreviousState;
        bool notReadyStarted = stateText == "NOT READY" && (firstState || previousStateText != "NOT READY");
        bool handLost = hasPreviousState && previousHandTracked && !handTracked;
        bool stopped = hasPreviousState
            && (previousDriveActive || previousPublishing)
            && !driveActive
            && stateText != "NOT READY";
        bool activeStarted = driveActive && (firstState || !previousDriveActive);
        bool publishingStarted = publishing && (firstState || !previousPublishing);
        bool holdStarted = gestureHolding && (firstState || !previousGestureHolding);
        bool handDetected = handTracked && (firstState || !previousHandTracked);
        bool readyStarted = stateText == "READY" && (firstState || previousStateText != "READY");

        if (notReadyStarted)
        {
            pendingPublishingNotification = publishingStarted;
            return "ROVER CONTROL NOT READY";
        }

        if (handLost)
        {
            pendingPublishingNotification = publishingStarted;
            return "RIGHT HAND NOT DETECTED";
        }

        if (stopped)
        {
            pendingPublishingNotification = publishingStarted;
            return "ROVER CONTROL STOPPED";
        }

        if (activeStarted)
        {
            pendingPublishingNotification = publishingStarted;
            return "ROVER CONTROL ACTIVE";
        }

        if (pendingPublishingNotification && publishing && !showingPriorityNotification)
        {
            pendingPublishingNotification = false;
            return "COMMAND PUBLISHING";
        }

        if (publishingStarted)
        {
            if (showingPriorityNotification)
            {
                pendingPublishingNotification = true;
                return string.Empty;
            }

            pendingPublishingNotification = false;
            return "COMMAND PUBLISHING";
        }

        if (holdStarted)
        {
            pendingPublishingNotification = publishingStarted;
            return "HOLD TO ACTIVATE";
        }

        if (handDetected)
        {
            pendingPublishingNotification = publishingStarted;
            return "RIGHT HAND DETECTED";
        }

        if (readyStarted)
        {
            pendingPublishingNotification = publishingStarted;
            return "ROVER CONTROL READY";
        }

        return string.Empty;
    }

    private void ShowNotification(string message)
    {
        if (statusText == null)
        {
            return;
        }

        Debug.Log(
            $"[RoverHUD] ShowNotification instance={GetInstanceID()} " +
            $"message={message}");

        statusText.text = message;
        showingPriorityNotification = message != "COMMAND PUBLISHING";

        if (notificationCanvas != null)
        {
            notificationCanvas.enabled = true;
        }

        if (notificationPanel != null)
        {
            notificationPanel.SetActive(true);
        }

        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
            hideRoutine = null;
        }

        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSecondsRealtime(notificationDurationSec);
        hideRoutine = null;
        HideNotification();

        if (pendingPublishingNotification && roverController != null && roverController.IsPublishing)
        {
            pendingPublishingNotification = false;
            ShowNotification("COMMAND PUBLISHING");
        }
    }

    private void HideNotification()
    {
        showingPriorityNotification = false;

        if (notificationPanel != null)
        {
            notificationPanel.SetActive(false);
        }

        if (notificationCanvas != null)
        {
            notificationCanvas.enabled = false;
        }
    }

    private static bool HasAnyHudInstanceIncludingInactive()
    {
        return FindObjectsByType<RoverControlStatusHUD>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None).Length > 0;
    }
}
