using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RoverControlStatusHUD : MonoBehaviour
{
    private static RoverControlStatusHUD instance;
    private static readonly HashSet<string> AllowedDirectionLabels = new HashSet<string>
    {
        "FORWARD",
        "BACK",
        "LEFT",
        "RIGHT",
        "STOP"
    };

    [SerializeField] private RightHandMecanumControl roverController;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private float refreshRateHz = 10f;
    [SerializeField] private float hudDirectionThreshold = 0.01f;
    [SerializeField] private bool enableDirectionLog;
    [SerializeField] private bool followHead = true;
    [SerializeField] private Vector3 localOffset = new Vector3(0.32f, 0.18f, 0.7f);

    private const float CanvasScale = 0.0015f;
    private const float PanelWidth = 560f;
    private const float PanelHeight = 120f;

    private Transform cachedHead;
    private Canvas notificationCanvas;
    private GameObject notificationPanel;
    private float nextRefreshTime;
    private float nextDirectionLogTime;
    private string lastHudDirectionLabel;
    private string hiddenTextNames = string.Empty;
    private bool startupLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeHud()
    {
        var existingHuds = FindObjectsByType<RoverControlStatusHUD>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (existingHuds.Length > 0)
        {
            existingHuds[0].gameObject.SetActive(true);
            for (int i = 1; i < existingHuds.Length; i++)
            {
                if (existingHuds[i] != null)
                {
                    Destroy(existingHuds[i].gameObject);
                }
            }

            return;
        }

        if (instance != null)
        {
            return;
        }

        var hudObject = new GameObject("RoverControlStatusHUD");
        hudObject.AddComponent<RoverControlStatusHUD>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        ResolveControllerOnce();
        EnsureNotificationView();
        NormalizeDirectionTextHierarchy();
        UpdateRoverDirectionHud("STOP");
        LogStartupState();
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
        lastHudDirectionLabel = null;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        UpdateDirectionDisplay();
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
        statusText.gameObject.name = "RoverDirectionText";
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.white;
        statusText.fontSize = 34f;
        statusText.fontStyle = FontStyles.Bold;
        statusText.enableWordWrapping = false;
        statusText.raycastTarget = false;
    }

    private void NormalizeDirectionTextHierarchy()
    {
        if (statusText == null)
        {
            return;
        }

        statusText.gameObject.name = "RoverDirectionText";
        statusText.text = "STOP";
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.enableWordWrapping = false;
        statusText.maxVisibleLines = 1;
        statusText.gameObject.SetActive(true);

        Transform textRoot = notificationPanel != null ? notificationPanel.transform : statusText.transform.parent;
        if (textRoot == null)
        {
            hiddenTextNames = string.Empty;
            return;
        }

        TMP_Text[] textComponents = textRoot.GetComponentsInChildren<TMP_Text>(true);
        var hiddenNames = new List<string>();
        for (int i = 0; i < textComponents.Length; i++)
        {
            TMP_Text textComponent = textComponents[i];
            if (textComponent == null || textComponent == statusText)
            {
                continue;
            }

            hiddenNames.Add(textComponent.gameObject.name);
            textComponent.text = string.Empty;
            textComponent.gameObject.SetActive(false);
        }

        HideLegacyPopupObjectByName("RightHandMecanumPopupText", hiddenNames);
        HideLegacyPopupObjectByName("RightHandMecanumPopupPanel", hiddenNames);
        HideLegacyPopupObjectByName("RightHandMecanumPopupCanvas", hiddenNames);

        hiddenTextNames = hiddenNames.Count > 0 ? string.Join(",", hiddenNames) : "None";
    }

    private void HideLegacyPopupObjectByName(string objectName, List<string> hiddenNames)
    {
        Transform[] transforms = FindObjectsByType<Transform>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.gameObject.name != objectName)
            {
                continue;
            }

            TMP_Text text = candidate.GetComponent<TMP_Text>();
            if (text != null)
            {
                text.text = string.Empty;
            }

            hiddenNames.Add(candidate.gameObject.name);
            candidate.gameObject.SetActive(false);
        }
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

    private void UpdateDirectionDisplay()
    {
        if (roverController == null)
        {
            ResolveControllerOnce();
        }

        if (roverController == null)
        {
            return;
        }

        bool active = roverController.IsDriveActive && roverController.IsCommandActive;
        float dx = roverController.StrafeInput;
        float dz = roverController.ForwardInput;
        string label = GetRoverDirectionLabel(dx, dz, active);
        UpdateRoverDirectionHud(label);
        LogDirection(label, active, dx, dz);
    }

    private string GetRoverDirectionLabel(float dx, float dz, bool active)
    {
        if (!active)
        {
            return "STOP";
        }

        float absDx = Mathf.Abs(dx);
        float absDz = Mathf.Abs(dz);
        float threshold = Mathf.Max(0f, hudDirectionThreshold);
        if (absDx < threshold && absDz < threshold)
        {
            return "STOP";
        }

        if (absDz >= absDx)
        {
            return dz > 0f ? "FORWARD" : "BACK";
        }

        return dx > 0f ? "RIGHT" : "LEFT";
    }

    private void UpdateRoverDirectionHud(string label)
    {
        label = SanitizeDirectionLabel(label);
        if (statusText == null || label == lastHudDirectionLabel)
        {
            return;
        }

        statusText.text = label;
        lastHudDirectionLabel = label;

        if (notificationCanvas != null)
        {
            notificationCanvas.enabled = true;
        }

        if (notificationPanel != null)
        {
            notificationPanel.SetActive(true);
        }
    }

    private static string SanitizeDirectionLabel(string label)
    {
        if (string.IsNullOrEmpty(label))
        {
            return "STOP";
        }

        string normalizedLabel = label.Trim().ToUpperInvariant();
        return AllowedDirectionLabels.Contains(normalizedLabel) ? normalizedLabel : "STOP";
    }

    private void LogDirection(string label, bool active, float dx, float dz)
    {
        if (!enableDirectionLog || Time.unscaledTime < nextDirectionLogTime)
        {
            return;
        }

        Debug.Log(
            "[RoverHUDDirection] label=" + label +
            " active=" + active +
            " dx=" + dx.ToString("F3") +
            " dz=" + dz.ToString("F3"));
        nextDirectionLogTime = Time.unscaledTime + 0.5f;
    }

    private void LogStartupState()
    {
        if (startupLogged)
        {
            return;
        }

        startupLogged = true;
        int activeInstances = FindObjectsByType<RoverControlStatusHUD>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None).Length;
        string directionTextName = statusText != null ? statusText.gameObject.name : "None";
        string currentLabel = SanitizeDirectionLabel(lastHudDirectionLabel);
        Debug.Log(
            "[RoverHUD] instances=" + activeInstances +
            " directionText=" + directionTextName +
            " hiddenTexts=" + hiddenTextNames +
            " label=" + currentLabel);
    }
}
