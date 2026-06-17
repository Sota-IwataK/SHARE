using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DisallowMultipleComponent]
public class PhotonSharedMRLoginPanel : MonoBehaviour
{
    [Header("Session")]
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public RoleBasedInfoFilter roleFilter;
    public PhotonSharedMRSessionSettings defaultSettings = PhotonSharedMRSessionSettings.CreateDefault();
    public bool suppressBootstrapAutoJoin = true;
    public bool hidePanelAfterStart = true;

    [Header("Panel Placement")]
    public float spawnDistance = 0.85f;
    public float verticalOffset = -0.08f;
    public float panelScale = 0.0015f;

    [Header("UI References")]
    public GameObject panelRoot;
    public TMP_InputField userNameInput;
    public TMP_Dropdown hostModeDropdown;
    public TMP_Dropdown deviceTypeDropdown;
    public TMP_Dropdown roleDropdown;
    public TMP_Dropdown robotTargetDropdown;
    public TMP_InputField roomNameInput;
    public Button startButton;
    public TMP_Text statusText;

    private bool uiInitialized;
    private bool startRequested;

    private void Awake()
    {
        ResolveSceneReferences();
        if (suppressBootstrapAutoJoin && bootstrap != null)
        {
            bootstrap.autoJoinOnStart = false;
        }

        EnsureUi();
        PopulateUi(defaultSettings);
        SetPanelVisible(true);
    }

    private void LateUpdate()
    {
        if (panelRoot == null || !panelRoot.activeSelf || Camera.main == null)
        {
            return;
        }

        Transform cameraTransform = Camera.main.transform;
        Transform panelTransform = panelRoot.transform;
        panelTransform.position = cameraTransform.position
            + cameraTransform.forward.normalized * spawnDistance
            + Vector3.up * verticalOffset;
        panelTransform.localScale = Vector3.one * Mathf.Max(0.0001f, panelScale);

        Vector3 forward = panelTransform.position - cameraTransform.position;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = cameraTransform.forward;
        }

        panelTransform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    public void StartSessionFromUi()
    {
        PhotonSharedMRSessionSettings settings = CollectSettingsFromUi();
        _ = StartSessionWithSettings(settings);
    }

    public async Task StartSessionWithSettings(PhotonSharedMRSessionSettings settings)
    {
        if (startRequested)
        {
            return;
        }

        startRequested = true;
        settings ??= PhotonSharedMRSessionSettings.CreateDefault();
        settings.Sanitize();

        SetStatus("Joining " + settings.roomName + " ...");
        SetStartInteractable(false);

        if (roleFilter != null)
        {
            roleFilter.SetManualRole(settings.role);
        }

        if (bootstrap == null)
        {
            ResolveSceneReferences();
        }

        if (bootstrap == null)
        {
            SetStatus("Photon bootstrap is missing.");
            SetStartInteractable(true);
            startRequested = false;
            Debug.LogError("[PhotonSharedMRLoginPanel] PhotonFusionSharedRoomBootstrap was not found.");
            return;
        }

        await bootstrap.StartSharedRoom(settings);

        if (bootstrap.IsRunning)
        {
            SetStatus("Joined " + settings.roomName);
            if (hidePanelAfterStart)
            {
                SetPanelVisible(false);
                enabled = false;
            }
        }
        else
        {
            SetStatus("Join failed. See Console.");
            SetStartInteractable(true);
            startRequested = false;
        }
    }

    public void SetPanelVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
        }
    }

    public PhotonSharedMRSessionSettings CollectSettingsFromUi()
    {
        PhotonSharedMRSessionSettings settings = defaultSettings != null
            ? defaultSettings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();

        settings.userName = userNameInput != null ? userNameInput.text : settings.userName;
        settings.isHostLikeUser = hostModeDropdown == null || hostModeDropdown.value == 0;
        settings.deviceType = ReadDropdownEnum(deviceTypeDropdown, settings.deviceType);
        settings.role = ReadDropdownEnum(roleDropdown, settings.role);
        settings.robotTarget = ReadDropdownEnum(robotTargetDropdown, settings.robotTarget);
        settings.roomName = roomNameInput != null ? roomNameInput.text : settings.roomName;
        settings.Sanitize();
        return settings;
    }

    private void ResolveSceneReferences()
    {
        if (bootstrap == null)
        {
            bootstrap = FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        }

        if (roleFilter == null)
        {
            roleFilter = FindObjectOfType<RoleBasedInfoFilter>(true);
        }
    }

    private void EnsureUi()
    {
        if (uiInitialized)
        {
            return;
        }

        if (panelRoot == null)
        {
            panelRoot = CreatePanelUi();
        }

        EnsureEventSystem();
        WireStartButton();
        uiInitialized = true;
    }

    private GameObject CreatePanelUi()
    {
        GameObject canvasObject = new GameObject(
            "LoginPanelCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 90;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(520f, 620f);
        canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, panelScale);

        GameObject panel = CreateUiObject("LoginPanel", canvasObject.transform, typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.03f, 0.06f, 0.08f, 0.94f);

        CreateLabel(panel.transform, "Title", "Photon Shared MR Login", 28, new Vector2(0f, 255f), new Vector2(460f, 46f), FontStyles.Bold);

        userNameInput = CreateInput(panel.transform, "UserNameInput", "UserName", new Vector2(0f, 190f));
        hostModeDropdown = CreateDropdown(panel.transform, "HostModeDropdown", "Start as Host-like", new[] { "Host-like", "Client-like" }, new Vector2(0f, 125f));
        deviceTypeDropdown = CreateEnumDropdown(panel.transform, "DeviceTypeDropdown", typeof(ShareDeviceType), new Vector2(0f, 60f));
        roleDropdown = CreateEnumDropdown(panel.transform, "RoleDropdown", typeof(SharedUserRole), new Vector2(0f, -5f));
        robotTargetDropdown = CreateEnumDropdown(panel.transform, "RobotTargetDropdown", typeof(SharedMRRobotTarget), new Vector2(0f, -70f));
        roomNameInput = CreateInput(panel.transform, "RoomNameInput", "RoomName", new Vector2(0f, -135f));
        startButton = CreateButton(panel.transform, "StartButton", "Start", new Vector2(0f, -215f));
        statusText = CreateLabel(panel.transform, "StatusText", "Waiting for Start", 18, new Vector2(0f, -275f), new Vector2(460f, 36f), FontStyles.Normal);

        return canvasObject;
    }

    private void PopulateUi(PhotonSharedMRSessionSettings settings)
    {
        settings ??= PhotonSharedMRSessionSettings.CreateDefault();
        settings.Sanitize();

        if (userNameInput != null)
        {
            userNameInput.text = settings.userName;
        }

        if (hostModeDropdown != null)
        {
            hostModeDropdown.value = settings.isHostLikeUser ? 0 : 1;
        }

        SelectEnumDropdownValue(deviceTypeDropdown, settings.deviceType);
        SelectEnumDropdownValue(roleDropdown, settings.role);
        SelectEnumDropdownValue(robotTargetDropdown, settings.robotTarget);

        if (roomNameInput != null)
        {
            roomNameInput.text = settings.roomName;
        }
    }

    private void WireStartButton()
    {
        if (startButton == null)
        {
            return;
        }

        startButton.onClick.RemoveListener(StartSessionFromUi);
        startButton.onClick.AddListener(StartSessionFromUi);
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[PhotonSharedMRLoginPanel] " + message);
    }

    private void SetStartInteractable(bool interactable)
    {
        if (startButton != null)
        {
            startButton.interactable = interactable;
        }
    }

    private static T ReadDropdownEnum<T>(TMP_Dropdown dropdown, T fallback) where T : struct, Enum
    {
        if (dropdown == null || dropdown.options == null || dropdown.options.Count == 0)
        {
            return fallback;
        }

        string option = dropdown.options[Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1)].text;
        return Enum.TryParse(option, out T result) ? result : fallback;
    }

    private static void SelectEnumDropdownValue<T>(TMP_Dropdown dropdown, T value) where T : struct, Enum
    {
        if (dropdown == null)
        {
            return;
        }

        string valueName = value.ToString();
        for (int i = 0; i < dropdown.options.Count; i++)
        {
            if (dropdown.options[i].text == valueName)
            {
                dropdown.value = i;
                dropdown.RefreshShownValue();
                return;
            }
        }
    }

    private static TMP_Dropdown CreateEnumDropdown(Transform parent, string objectName, Type enumType, Vector2 anchoredPosition)
    {
        string[] names = Enum.GetNames(enumType);
        return CreateDropdown(parent, objectName, enumType.Name, names, anchoredPosition);
    }

    private static TMP_Dropdown CreateDropdown(Transform parent, string objectName, string label, IEnumerable<string> options, Vector2 anchoredPosition)
    {
        CreateLabel(parent, objectName + "Label", label, 16, anchoredPosition + new Vector2(0f, 29f), new Vector2(430f, 24f), FontStyles.Normal);

        GameObject dropdownObject = CreateUiObject(objectName, parent, typeof(Image), typeof(TMP_Dropdown));
        RectTransform rect = dropdownObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, anchoredPosition, new Vector2(430f, 44f));

        Image image = dropdownObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.14f, 0.18f, 0.98f);

        TMP_Text caption = CreateLabel(dropdownObject.transform, "Label", string.Empty, 19, Vector2.zero, new Vector2(360f, 38f), FontStyles.Normal);
        caption.alignment = TextAlignmentOptions.MidlineLeft;
        caption.rectTransform.offsetMin = new Vector2(16f, 0f);
        caption.rectTransform.offsetMax = new Vector2(-54f, 0f);

        TMP_Text arrow = CreateLabel(dropdownObject.transform, "Arrow", "v", 22, new Vector2(190f, 0f), new Vector2(34f, 38f), FontStyles.Bold);
        RectTransform template = CreateDropdownTemplate(dropdownObject.transform);

        TMP_Dropdown dropdown = dropdownObject.GetComponent<TMP_Dropdown>();
        dropdown.captionText = caption;
        dropdown.template = template;
        dropdown.itemText = template.GetComponentInChildren<TMP_Text>(true);
        dropdown.targetGraphic = image;
        dropdown.options.Clear();
        foreach (string option in options)
        {
            dropdown.options.Add(new TMP_Dropdown.OptionData(option));
        }
        dropdown.RefreshShownValue();
        return dropdown;
    }

    private static RectTransform CreateDropdownTemplate(Transform dropdownTransform)
    {
        GameObject templateObject = CreateUiObject("Template", dropdownTransform, typeof(Image), typeof(ScrollRect));
        RectTransform templateRect = templateObject.GetComponent<RectTransform>();
        templateRect.anchorMin = new Vector2(0f, 0f);
        templateRect.anchorMax = new Vector2(1f, 0f);
        templateRect.pivot = new Vector2(0.5f, 1f);
        templateRect.anchoredPosition = new Vector2(0f, -46f);
        templateRect.sizeDelta = new Vector2(0f, 190f);

        Image templateImage = templateObject.GetComponent<Image>();
        templateImage.color = new Color(0.05f, 0.09f, 0.12f, 0.98f);

        GameObject viewportObject = CreateUiObject("Viewport", templateObject.transform, typeof(Image), typeof(Mask));
        RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;
        Image viewportImage = viewportObject.GetComponent<Image>();
        viewportImage.color = new Color(1f, 1f, 1f, 0.03f);
        Mask mask = viewportObject.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject contentObject = CreateUiObject("Content", viewportObject.transform);
        RectTransform contentRect = contentObject.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 180f);

        GameObject itemObject = CreateUiObject("Item", contentObject.transform, typeof(Toggle));
        RectTransform itemRect = itemObject.GetComponent<RectTransform>();
        itemRect.anchorMin = new Vector2(0f, 1f);
        itemRect.anchorMax = new Vector2(1f, 1f);
        itemRect.pivot = new Vector2(0.5f, 1f);
        itemRect.anchoredPosition = Vector2.zero;
        itemRect.sizeDelta = new Vector2(0f, 38f);

        GameObject itemBackground = CreateUiObject("Item Background", itemObject.transform, typeof(Image));
        RectTransform backgroundRect = itemBackground.GetComponent<RectTransform>();
        backgroundRect.anchorMin = Vector2.zero;
        backgroundRect.anchorMax = Vector2.one;
        backgroundRect.offsetMin = Vector2.zero;
        backgroundRect.offsetMax = Vector2.zero;
        Image backgroundImage = itemBackground.GetComponent<Image>();
        backgroundImage.color = new Color(0.08f, 0.14f, 0.18f, 0.98f);

        TMP_Text itemLabel = CreateLabel(itemObject.transform, "Item Label", "Option", 18, Vector2.zero, new Vector2(390f, 34f), FontStyles.Normal);
        itemLabel.alignment = TextAlignmentOptions.MidlineLeft;
        itemLabel.rectTransform.offsetMin = new Vector2(14f, 0f);
        itemLabel.rectTransform.offsetMax = new Vector2(-12f, 0f);

        Toggle toggle = itemObject.GetComponent<Toggle>();
        toggle.targetGraphic = backgroundImage;
        toggle.graphic = null;

        ScrollRect scrollRect = templateObject.GetComponent<ScrollRect>();
        scrollRect.viewport = viewportRect;
        scrollRect.content = contentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;

        templateObject.SetActive(false);
        return templateRect;
    }

    private static TMP_InputField CreateInput(Transform parent, string objectName, string label, Vector2 anchoredPosition)
    {
        CreateLabel(parent, objectName + "Label", label, 16, anchoredPosition + new Vector2(0f, 29f), new Vector2(430f, 24f), FontStyles.Normal);

        GameObject inputObject = CreateUiObject(objectName, parent, typeof(Image), typeof(TMP_InputField));
        RectTransform rect = inputObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, anchoredPosition, new Vector2(430f, 44f));

        Image image = inputObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.14f, 0.18f, 0.98f);

        TMP_Text text = CreateLabel(inputObject.transform, "Text", string.Empty, 20, Vector2.zero, new Vector2(390f, 38f), FontStyles.Normal);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        TMP_Text placeholder = CreateLabel(inputObject.transform, "Placeholder", label, 20, Vector2.zero, new Vector2(390f, 38f), FontStyles.Italic);
        placeholder.color = new Color(0.55f, 0.62f, 0.68f, 0.7f);
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.textViewport = rect;
        input.textComponent = text;
        input.placeholder = placeholder;
        input.characterLimit = 64;
        input.selectionColor = new Color(0.2f, 0.65f, 0.95f, 0.55f);
        return input;
    }

    private static Button CreateButton(Transform parent, string objectName, string text, Vector2 anchoredPosition)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent, typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, anchoredPosition, new Vector2(210f, 54f));

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.05f, 0.34f, 0.42f, 0.98f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.08f, 0.48f, 0.58f, 1f);
        colors.pressedColor = new Color(0.02f, 0.25f, 0.32f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        CreateLabel(buttonObject.transform, "Label", text, 23, Vector2.zero, new Vector2(200f, 48f), FontStyles.Bold);
        return button;
    }

    private static TMP_Text CreateLabel(Transform parent, string objectName, string text, int fontSize, Vector2 anchoredPosition, Vector2 size, FontStyles style)
    {
        GameObject labelObject = CreateUiObject(objectName, parent, typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, anchoredPosition, size);

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        return label;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent, params Type[] componentTypes)
    {
        GameObject obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        obj.transform.SetParent(parent, false);
        for (int i = 0; i < componentTypes.Length; i++)
        {
            if (componentTypes[i] != null && obj.GetComponent(componentTypes[i]) == null)
            {
                obj.AddComponent(componentTypes[i]);
            }
        }

        return obj;
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>(true);
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("PhotonSharedMRLoginEventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
    }
}
