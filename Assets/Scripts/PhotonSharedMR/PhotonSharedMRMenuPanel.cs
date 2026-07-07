using System;
using System.Threading.Tasks;
using MixedReality.Toolkit;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DisallowMultipleComponent]
public class PhotonSharedMRMenuPanel : MonoBehaviour
{
    private const string PanelObjectName = "PhotonSharedMRMenuSection";

    [Header("Scene References")]
    public RectTransform existingMenuRoot;
    public PhotonSharedMRLoginPanel loginPanel;
    public PhotonSharedMRLoginPanelVisibilityController loginPanelVisibilityController;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public RoleBasedInfoFilter roleFilter;
    public PhotonSharedBottleSpawner bottleSpawner;
    public PhotonSharedMRDebugPanel debugPanel;

    [Header("Existing Menu Placement")]
    public bool enableMenuPanel = true;
    public bool showOnStart = false;
    public bool collapseSettingsAfterJoin = true;
    public Vector2 panelAnchoredPosition = new Vector2(145f, -12f);
    public Vector2 panelSize = new Vector2(292f, 620f);
    public bool enableMenuDebugLogs;
    public bool allowLegacyJoinControls;

    [Header("HandMenu Entry")]
    public bool connectExistingPhotonSettingButton = true;
    public string[] photonSettingButtonObjectNames =
    {
        "Photon setting",
        "Photon Setting"
    };

    [Header("Runtime UI References")]
    public GameObject panelRoot;
    public GameObject headerGroup;
    public GameObject notConnectedPanel;
    public GameObject joinedPanel;
    public GameObject errorPanel;
    public GameObject disconnectedGroup;
    public GameObject settingsGroup;
    public GameObject operationsGroup;
    public Button openLoginButton;
    public TMP_Text headerStatusText;
    public TMP_Text disconnectedStatusText;
    public TMP_InputField userNameInput;
    public TMP_InputField roomNameInput;
    public Button hostModeButton;
    public TMP_Text hostModeLabel;
    public Button deviceTypeButton;
    public TMP_Text deviceTypeLabel;
    public Button roleButton;
    public TMP_Text roleLabel;
    public Button robotTargetButton;
    public TMP_Text robotTargetLabel;
    public Button startButton;
    public Button retryButton;
    public Button spawnButton;
    public Button despawnButton;
    public Button debugToggleButton;
    public TMP_Text debugToggleLabel;
    public Button leaveRoomButton;
    public Button closeButton;
    public TMP_Text protocolText;
    public TMP_Text fixedRegionText;
    public TMP_Text activePlayersText;
    public TMP_Text statusText;
    public TMP_Text errorText;

    private bool isHostLikeUser = true;
    private ShareDeviceType deviceType = ShareDeviceType.PCEditor;
    private SharedUserRole role = SharedUserRole.ManipulatorOperator;
    private SharedMRRobotTarget robotTarget = SharedMRRobotTarget.Amir;
    private bool uiReady;
    private bool visibilityInitialized;
    private bool useWorldFixedPose;
    private Vector3 fixedWorldPosition;
    private Quaternion fixedWorldRotation = Quaternion.identity;
    private float fixedWorldScale = 0.001f;
    private bool startInProgress;
    private float nextStatusRefreshTime;
    private int lastEntryToggleFrame = -1;

    public bool IsMenuVisible => panelRoot != null && panelRoot.activeSelf;
    public Transform PanelTransform => panelRoot != null ? panelRoot.transform : null;

    private void Awake()
    {
        ResolveReferences();
        EnsurePanel();
        ApplyDefaultSettings();
        WireButtons();
        RefreshStatus(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsurePanel();
        WireButtons();
        RefreshStatus(true);
    }

    private void OnDisable()
    {
        UnwireButtons();
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextStatusRefreshTime)
        {
            RefreshStatus(false);
            nextStatusRefreshTime = Time.unscaledTime + 0.2f;
        }
    }

    private void LateUpdate()
    {
        if (useWorldFixedPose && panelRoot != null && panelRoot.activeSelf)
        {
            ApplyFixedWorldPose();
        }
    }

    public void EnsurePanel()
    {
        if (!enableMenuPanel)
        {
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            return;
        }

        RectTransform menuRoot = ResolveMenuRoot();
        if (menuRoot == null)
        {
            Debug.LogWarning("[PhotonSharedMRMenuPanel] Existing menu root was not found; Photon menu UI was not created.");
            return;
        }

        DestroyDuplicatePanels(menuRoot);

        if (panelRoot == null)
        {
            Transform existing = menuRoot.Find(PanelObjectName);
            panelRoot = existing != null ? existing.gameObject : CreatePanel(menuRoot);
        }

        panelRoot.transform.SetParent(menuRoot, false);
        ConfigurePanelRect(panelRoot.GetComponent<RectTransform>());
        ResolvePanelReferences();
        if (!HasRequiredPanelStructure())
        {
            DestroyObject(panelRoot);
            panelRoot = CreatePanel(menuRoot);
            ConfigurePanelRect(panelRoot.GetComponent<RectTransform>());
            ResolvePanelReferences();
        }

        EnsureEventSystem();
        uiReady = true;
        WirePhotonSettingEntryButton();

        if (!visibilityInitialized)
        {
            panelRoot.SetActive(showOnStart);
            visibilityInitialized = true;
        }
    }

    public void SetMenuVisible(bool visible)
    {
        ResolveReferences();
        EnsurePanel();
        if (panelRoot == null)
        {
            return;
        }

        bool changed = panelRoot.activeSelf != visible;
        panelRoot.SetActive(visible);
        if (visible)
        {
            RefreshStatus(true);
        }
        else
        {
            useWorldFixedPose = false;
        }

        if (changed && enableMenuDebugLogs)
        {
            Debug.Log(visible ? "PHOTON_MENU_OPEN" : "PHOTON_MENU_CLOSE");
        }
    }

    public void ToggleMenuVisible()
    {
        SetMenuVisible(!IsMenuVisible);
    }

    public void ToggleMenuVisibleFromEntry()
    {
        if (lastEntryToggleFrame == Time.frameCount)
        {
            return;
        }

        lastEntryToggleFrame = Time.frameCount;
        ToggleMenuVisible();
    }

    public void ToggleAtHead(
        Transform head,
        float distanceFromHead,
        float horizontalLeftOffset,
        float verticalDownOffset,
        float worldScale,
        bool faceCamera)
    {
        if (IsMenuVisible)
        {
            CloseMenu();
            return;
        }

        OpenAtHead(head, distanceFromHead, horizontalLeftOffset, verticalDownOffset, worldScale, faceCamera);
    }

    public void OpenAtHead(
        Transform head,
        float distanceFromHead,
        float horizontalLeftOffset,
        float verticalDownOffset,
        float worldScale,
        bool faceCamera)
    {
        ResolveReferences();
        EnsurePanel();
        if (panelRoot == null)
        {
            return;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        if (head == null)
        {
            SetMenuVisible(true);
            return;
        }

        Vector3 forward = head.forward.sqrMagnitude > 0.0001f ? head.forward.normalized : Vector3.forward;
        Vector3 right = head.right.sqrMagnitude > 0.0001f ? head.right.normalized : Vector3.right;
        Vector3 up = head.up.sqrMagnitude > 0.0001f ? head.up.normalized : Vector3.up;

        fixedWorldPosition = head.position
            + forward * Mathf.Max(0.05f, distanceFromHead)
            - right * horizontalLeftOffset
            - up * verticalDownOffset;

        if (faceCamera)
        {
            Vector3 facing = fixedWorldPosition - head.position;
            if (facing.sqrMagnitude < 0.0001f)
            {
                facing = forward;
            }

            fixedWorldRotation = Quaternion.LookRotation(facing, Vector3.up);
        }
        else
        {
            fixedWorldRotation = panelRoot.transform.rotation;
        }

        fixedWorldScale = Mathf.Max(0.00001f, worldScale);
        useWorldFixedPose = true;
        SetMenuVisible(true);
        ApplyFixedWorldPose();
    }

    public void CloseMenu()
    {
        SetMenuVisible(false);
    }

    private void ApplyFixedWorldPose()
    {
        if (panelRoot == null)
        {
            return;
        }

        Transform panelTransform = panelRoot.transform;
        panelTransform.SetPositionAndRotation(fixedWorldPosition, fixedWorldRotation);
        SetWorldScale(panelTransform, fixedWorldScale);
    }

    private static void SetWorldScale(Transform target, float uniformWorldScale)
    {
        if (target == null)
        {
            return;
        }

        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(
            SafeInverseScale(parentScale.x) * uniformWorldScale,
            SafeInverseScale(parentScale.y) * uniformWorldScale,
            SafeInverseScale(parentScale.z) * uniformWorldScale);
    }

    private static float SafeInverseScale(float scale)
    {
        return Mathf.Abs(scale) > 0.00001f ? 1f / scale : 1f;
    }

    public void StartOrJoinFromMenu()
    {
        if (!CanUseLegacyJoinControl(nameof(StartOrJoinFromMenu)))
        {
            return;
        }

        _ = StartOrJoinAsync(false);
    }

    public void RetryFromMenu()
    {
        if (!CanUseLegacyJoinControl(nameof(RetryFromMenu)))
        {
            return;
        }

        ResolveReferences();
        if (loginPanel != null)
        {
            loginPanel.RetrySessionFromUi();
            RefreshStatus(true);
            return;
        }

        _ = StartOrJoinAsync(true);
    }

    public void ToggleHostMode()
    {
        isHostLikeUser = !isHostLikeUser;
        RefreshChoiceLabels();
    }

    public void CycleDeviceType()
    {
        deviceType = CycleEnum(deviceType);
        RefreshChoiceLabels();
    }

    public void CycleRole()
    {
        role = CycleEnum(role);
        if (roleFilter != null)
        {
            roleFilter.SetManualRole(role);
        }

        RefreshChoiceLabels();
    }

    public void CycleRobotTarget()
    {
        robotTarget = CycleEnum(robotTarget);
        RefreshChoiceLabels();
    }

    public void ToggleDebugPanel()
    {
        ResolveReferences();
        if (debugPanel == null)
        {
            SetError("DebugPanel missing.");
            return;
        }

        debugPanel.enableDebugPanel = !debugPanel.enableDebugPanel;
        debugPanel.gameObject.SetActive(true);
        RefreshStatus(true);
    }

    public void LeaveRoomFromMenu()
    {
        ResolveReferences();
        SetError("Legacy Photon menu leave is disabled. Use PhotonButtonCollection Leave Room.");
        RefreshStatus(true);
    }

    private async Task StartOrJoinAsync(bool retry)
    {
        ResolveReferences();
        EnsurePanel();

        if (!CanUseLegacyJoinControl(nameof(StartOrJoinAsync)))
        {
            return;
        }

        if (startInProgress)
        {
            return;
        }

        if (bootstrap != null && bootstrap.IsRunning)
        {
            SetStatus("Already joined " + bootstrap.DebugRoomName);
            return;
        }

        PhotonSharedMRSessionSettings settings = CollectSettings();
        if (loginPanel != null)
        {
            loginPanel.defaultSettings = settings.Clone();
        }

        if (bootstrap != null)
        {
            bootstrap.defaultSessionSettings = settings.Clone();
        }

        startInProgress = true;
        SetStatus((retry ? "Retrying " : "Joining ") + settings.roomName + " ...");
        SetError(string.Empty);
        SetConnectionButtonsInteractable(false);

        if (roleFilter != null)
        {
            roleFilter.SetManualRole(settings.role);
        }

        SetError("Legacy Photon menu join is disabled. Use Startup Photon Login Panel.");

        startInProgress = false;
        RefreshStatus(true);
    }

    private bool CanUseLegacyJoinControl(string method)
    {
        if (allowLegacyJoinControls && enableMenuPanel && !PhotonSharedMRCalibrationGuard.CalibrationInProgress)
        {
            return true;
        }

        Debug.LogWarning("[PhotonSharedMRMenuPanel] PHOTON_LEGACY_JOIN_BLOCKED"
            + " method=" + method
            + " allowLegacyJoinControls=" + allowLegacyJoinControls
            + " enableMenuPanel=" + enableMenuPanel
            + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress);
        return false;
    }

    private PhotonSharedMRSessionSettings CollectSettings()
    {
        PhotonSharedMRSessionSettings settings = loginPanel != null && loginPanel.defaultSettings != null
            ? loginPanel.defaultSettings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();

        settings.userName = userNameInput != null ? userNameInput.text : settings.userName;
        settings.roomName = roomNameInput != null ? roomNameInput.text : settings.roomName;
        settings.isHostLikeUser = isHostLikeUser;
        settings.deviceType = deviceType;
        settings.role = role;
        settings.robotTarget = robotTarget;
        settings.Sanitize();
        return settings;
    }

    private void ApplyDefaultSettings()
    {
        PhotonSharedMRSessionSettings settings = loginPanel != null && loginPanel.defaultSettings != null
            ? loginPanel.defaultSettings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();
        settings.Sanitize();

        isHostLikeUser = settings.isHostLikeUser;
        deviceType = settings.deviceType;
        role = settings.role;
        robotTarget = settings.robotTarget;

        if (userNameInput != null)
        {
            userNameInput.text = settings.userName;
        }

        if (roomNameInput != null)
        {
            roomNameInput.text = settings.roomName;
        }

        RefreshChoiceLabels();
    }

    private void ResolveReferences()
    {
        if (loginPanel == null)
        {
            loginPanel = FindObjectOfType<PhotonSharedMRLoginPanel>(true);
        }

        if (loginPanelVisibilityController == null)
        {
            loginPanelVisibilityController = loginPanel != null && loginPanel.visibilityController != null
                ? loginPanel.visibilityController
                : FindObjectOfType<PhotonSharedMRLoginPanelVisibilityController>(true);
        }

        if (bootstrap == null)
        {
            bootstrap = loginPanel != null && loginPanel.bootstrap != null
                ? loginPanel.bootstrap
                : bootstrap;
        }

        EnsureBootstrap(nameof(ResolveReferences), false);

        if (roleFilter == null)
        {
            roleFilter = loginPanel != null && loginPanel.roleFilter != null
                ? loginPanel.roleFilter
                : FindObjectOfType<RoleBasedInfoFilter>(true);
        }

        if (bottleSpawner == null)
        {
            bottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        }

        if (debugPanel == null)
        {
            debugPanel = FindObjectOfType<PhotonSharedMRDebugPanel>(true);
        }

        if (loginPanel != null)
        {
            loginPanel.bootstrap = bootstrap;
            loginPanel.roleFilter = roleFilter;
        }
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private RectTransform ResolveMenuRoot()
    {
        RectTransform photonSettingRoot = ResolvePhotonSettingMenuRoot();
        if (photonSettingRoot != null)
        {
            existingMenuRoot = photonSettingRoot;
            return existingMenuRoot;
        }

        if (existingMenuRoot != null)
        {
            return existingMenuRoot;
        }

        GameObject uiMenu = GameObject.Find("UiMenuPrefab");
        if (uiMenu != null && uiMenu.TryGetComponent(out RectTransform rectTransform))
        {
            existingMenuRoot = rectTransform;
            return existingMenuRoot;
        }

        Canvas[] canvases = FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null
                && canvas.renderMode == RenderMode.WorldSpace
                && !IsReservedPhotonCanvas(canvas.transform))
            {
                existingMenuRoot = canvas.GetComponent<RectTransform>();
                if (existingMenuRoot != null)
                {
                    return existingMenuRoot;
                }
            }
        }

        return null;
    }

    private RectTransform ResolvePhotonSettingMenuRoot()
    {
        GameObject entryObject = FindPhotonSettingEntryObject();
        if (entryObject == null)
        {
            return null;
        }

        if (entryObject.TryGetComponent(out RectTransform entryRect))
        {
            return entryRect;
        }

        Transform parent = entryObject.transform.parent;
        while (parent != null)
        {
            if (parent.name == "MenuContent" && parent.TryGetComponent(out RectTransform menuContentRect))
            {
                return menuContentRect;
            }

            parent = parent.parent;
        }

        Transform directParent = entryObject.transform.parent;
        return directParent != null && directParent.TryGetComponent(out RectTransform parentRect)
            ? parentRect
            : null;
    }

    private static bool IsReservedPhotonCanvas(Transform canvasTransform)
    {
        if (canvasTransform == null)
        {
            return false;
        }

        return canvasTransform.name == "StartupPhotonLoginPanelCanvas"
            || canvasTransform.name == "LoginPanelCanvas"
            || canvasTransform.name == "SharedBottleSpawnerCanvas"
            || canvasTransform.name == "PhotonSharedMRHmdFrontSpawnUI"
            || canvasTransform.name == "HmdFrontSpawnUI";
    }

    private GameObject CreatePanel(RectTransform menuRoot)
    {
        GameObject panel = CreateUiObject(PanelObjectName, menuRoot, typeof(Image));
        Image image = panel.GetComponent<Image>();
        image.color = new Color(0.02f, 0.035f, 0.04f, 0.92f);

        headerGroup = CreateUiObject("Header", panel.transform);
        ConfigureCenteredRect(headerGroup.GetComponent<RectTransform>(), new Vector2(0f, 238f), new Vector2(270f, 96f));
        CreateLabel(headerGroup.transform, "Title", "Photon Shared MR", 19, new Vector2(-30f, 24f), new Vector2(188f, 28f), FontStyles.Bold);
        closeButton = CreateButton(headerGroup.transform, "CloseButton", "Close", new Vector2(96f, 24f), new Vector2(70f, 30f), out _);
        headerStatusText = CreateLabel(headerGroup.transform, "HeaderStatusText", "Status: Not Connected", 13, new Vector2(0f, -16f), new Vector2(256f, 40f), FontStyles.Normal);
        statusText = headerStatusText;

        notConnectedPanel = CreateUiObject("NotConnectedPanel", panel.transform);
        ConfigureCenteredRect(notConnectedPanel.GetComponent<RectTransform>(), new Vector2(0f, 112f), new Vector2(270f, 130f));
        disconnectedStatusText = CreateLabel(notConnectedPanel.transform, "NotConnectedStatusText", "Status: Not Connected", 15, new Vector2(0f, 28f), new Vector2(260f, 42f), FontStyles.Normal);
        openLoginButton = CreateButton(notConnectedPanel.transform, "OpenLoginButton", "Open Login", new Vector2(0f, -32f), new Vector2(246f, 36f), out _);

        joinedPanel = CreateUiObject("JoinedPanel", panel.transform);
        ConfigureCenteredRect(joinedPanel.GetComponent<RectTransform>(), new Vector2(0f, 12f), new Vector2(270f, 318f));
        activePlayersText = CreateLabel(joinedPanel.transform, "JoinedStatusText", "Room: -\nPlayers: 0\nRole: -", 13, new Vector2(0f, 110f), new Vector2(260f, 84f), FontStyles.Normal);
        spawnButton = CreateButton(joinedPanel.transform, "SpawnSharedBottleButton", "Spawn Shared Bottle", new Vector2(0f, 38f), new Vector2(246f, 34f), out _);
        despawnButton = CreateButton(joinedPanel.transform, "DespawnSharedBottleButton", "Despawn Last Shared Bottle", new Vector2(0f, -6f), new Vector2(246f, 34f), out _);
        debugToggleButton = CreateButton(joinedPanel.transform, "DebugPanelToggleButton", "Debug Panel ON/OFF", new Vector2(0f, -50f), new Vector2(246f, 34f), out debugToggleLabel);
        leaveRoomButton = CreateButton(joinedPanel.transform, "LeaveRoomButton", "Leave Room", new Vector2(0f, -94f), new Vector2(246f, 34f), out _);

        errorPanel = CreateUiObject("ErrorPanel", panel.transform);
        ConfigureCenteredRect(errorPanel.GetComponent<RectTransform>(), new Vector2(0f, -214f), new Vector2(270f, 112f));
        errorText = CreateLabel(errorPanel.transform, "LastErrorText", "Last Error: None", 11, new Vector2(0f, 26f), new Vector2(260f, 48f), FontStyles.Normal);
        errorText.color = new Color(1f, 0.55f, 0.45f, 1f);
        retryButton = CreateButton(errorPanel.transform, "RetryButton", "Retry", new Vector2(0f, -32f), new Vector2(246f, 34f), out _);

        return panel;
    }

    private void ConfigurePanelRect(RectTransform rect)
    {
        if (rect == null)
        {
            return;
        }

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = panelAnchoredPosition;
        rect.sizeDelta = panelSize;
        rect.localScale = Vector3.one;
    }

    private void ResolvePanelReferences()
    {
        if (panelRoot == null)
        {
            return;
        }

        headerGroup = FindChild(panelRoot.transform, "Header");
        notConnectedPanel = FindChild(panelRoot.transform, "NotConnectedPanel");
        joinedPanel = FindChild(panelRoot.transform, "JoinedPanel");
        errorPanel = FindChild(panelRoot.transform, "ErrorPanel");
        disconnectedGroup = FindChild(panelRoot.transform, "DisconnectedGroup");
        settingsGroup = FindChild(panelRoot.transform, "SettingsGroup");
        operationsGroup = FindChild(panelRoot.transform, "OperationsGroup");
        openLoginButton = FindChildComponent<Button>("OpenLoginButton");
        headerStatusText = FindChildComponent<TMP_Text>("HeaderStatusText");
        disconnectedStatusText = FindChildComponent<TMP_Text>("NotConnectedStatusText");
        userNameInput = FindChildComponent<TMP_InputField>("UserNameInput");
        roomNameInput = FindChildComponent<TMP_InputField>("RoomNameInput");
        hostModeButton = FindChildComponent<Button>("HostModeButton");
        hostModeLabel = FindChildLabel("HostModeButton");
        deviceTypeButton = FindChildComponent<Button>("DeviceTypeButton");
        deviceTypeLabel = FindChildLabel("DeviceTypeButton");
        roleButton = FindChildComponent<Button>("RoleButton");
        roleLabel = FindChildLabel("RoleButton");
        robotTargetButton = FindChildComponent<Button>("RobotTargetButton");
        robotTargetLabel = FindChildLabel("RobotTargetButton");
        startButton = FindChildComponent<Button>("StartJoinButton");
        retryButton = FindChildComponent<Button>("RetryButton");
        spawnButton = FindChildComponent<Button>("SpawnSharedBottleButton");
        despawnButton = FindChildComponent<Button>("DespawnSharedBottleButton");
        debugToggleButton = FindChildComponent<Button>("DebugPanelToggleButton");
        debugToggleLabel = FindChildLabel("DebugPanelToggleButton");
        leaveRoomButton = FindChildComponent<Button>("LeaveRoomButton");
        closeButton = FindChildComponent<Button>("CloseButton");
        protocolText = FindChildComponent<TMP_Text>("ProtocolText");
        fixedRegionText = FindChildComponent<TMP_Text>("FixedRegionText");
        activePlayersText = FindChildComponent<TMP_Text>("JoinedStatusText");
        statusText = headerStatusText;
        errorText = FindChildComponent<TMP_Text>("LastErrorText");
    }

    private bool HasRequiredPanelStructure()
    {
        return panelRoot != null
            && headerGroup != null
            && notConnectedPanel != null
            && joinedPanel != null
            && errorPanel != null
            && openLoginButton != null
            && retryButton != null
            && headerStatusText != null
            && disconnectedStatusText != null
            && spawnButton != null
            && despawnButton != null
            && debugToggleButton != null
            && leaveRoomButton != null
            && activePlayersText != null
            && statusText != null
            && errorText != null;
    }

    private void WireButtons()
    {
        if (!uiReady)
        {
            return;
        }

        WireButton(openLoginButton, OpenLoginFromMenu);
        WireButton(retryButton, RetryFromMenu);
        WireButton(spawnButton, RequestSpawnSharedBottle);
        WireButton(despawnButton, RequestDespawnSharedBottle);
        WireButton(debugToggleButton, ToggleDebugPanel);
        WireButton(leaveRoomButton, LeaveRoomFromMenu);
        WireButton(closeButton, CloseMenu);
        WirePhotonSettingEntryButton();
    }

    private void UnwireButtons()
    {
        if (openLoginButton != null) openLoginButton.onClick.RemoveListener(OpenLoginFromMenu);
        if (retryButton != null) retryButton.onClick.RemoveListener(RetryFromMenu);
        if (spawnButton != null) spawnButton.onClick.RemoveListener(RequestSpawnSharedBottle);
        if (despawnButton != null) despawnButton.onClick.RemoveListener(RequestDespawnSharedBottle);
        if (debugToggleButton != null) debugToggleButton.onClick.RemoveListener(ToggleDebugPanel);
        if (leaveRoomButton != null) leaveRoomButton.onClick.RemoveListener(LeaveRoomFromMenu);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseMenu);
        UnwirePhotonSettingEntryButton();
    }

    private void WirePhotonSettingEntryButton()
    {
        if (!connectExistingPhotonSettingButton)
        {
            return;
        }

        GameObject entryObject = FindPhotonSettingEntryObject();
        if (entryObject == null)
        {
            return;
        }

        Button[] buttons = entryObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || IsPanelChild(button.transform))
            {
                continue;
            }

            button.onClick.RemoveListener(ToggleMenuVisibleFromEntry);
            button.onClick.AddListener(ToggleMenuVisibleFromEntry);
        }

        StatefulInteractable[] interactables = entryObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            StatefulInteractable interactable = interactables[i];
            if (interactable == null || IsPanelChild(interactable.transform))
            {
                continue;
            }

            interactable.OnClicked.RemoveListener(ToggleMenuVisibleFromEntry);
            interactable.OnClicked.AddListener(ToggleMenuVisibleFromEntry);
        }
    }

    private void UnwirePhotonSettingEntryButton()
    {
        GameObject entryObject = FindPhotonSettingEntryObject();
        if (entryObject == null)
        {
            return;
        }

        Button[] buttons = entryObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button != null)
            {
                button.onClick.RemoveListener(ToggleMenuVisibleFromEntry);
            }
        }

        StatefulInteractable[] interactables = entryObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            StatefulInteractable interactable = interactables[i];
            if (interactable != null)
            {
                interactable.OnClicked.RemoveListener(ToggleMenuVisibleFromEntry);
            }
        }
    }

    private bool IsPanelChild(Transform candidate)
    {
        return panelRoot != null && candidate != null && candidate.IsChildOf(panelRoot.transform);
    }

    private GameObject FindPhotonSettingEntryObject()
    {
        if (photonSettingButtonObjectNames == null || photonSettingButtonObjectNames.Length == 0)
        {
            return null;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int nameIndex = 0; nameIndex < photonSettingButtonObjectNames.Length; nameIndex++)
        {
            string objectName = photonSettingButtonObjectNames[nameIndex];
            if (string.IsNullOrWhiteSpace(objectName))
            {
                continue;
            }

            for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                GameObject found = FindChild(roots[rootIndex].transform, objectName);
                if (found != null && !IsPanelChild(found.transform))
                {
                    return found;
                }
            }
        }

        return null;
    }

    private void OpenLoginFromMenu()
    {
        ResolveReferences();
        if (loginPanel == null)
        {
            SetError("Photon login panel missing.");
            return;
        }

        loginPanel.ResetStartRequest();
        loginPanel.OpenLoginPanel();
        RefreshStatus(true);
    }

    private void RequestSpawnSharedBottle()
    {
        ResolveReferences();
        if (bottleSpawner != null)
        {
            bottleSpawner.RequestSpawnInFrontOfHmd();
        }
    }

    private void RequestDespawnSharedBottle()
    {
        ResolveReferences();
        if (bottleSpawner != null)
        {
            bottleSpawner.RequestDespawnLastSharedBottle();
        }
    }

    private void RefreshStatus(bool force)
    {
        ResolveReferences();

        bool joined = bootstrap != null && bootstrap.IsRunning;
        if (notConnectedPanel != null)
        {
            notConnectedPanel.SetActive(!joined);
        }

        if (disconnectedGroup != null)
        {
            disconnectedGroup.SetActive(false);
        }

        if (settingsGroup != null)
        {
            settingsGroup.SetActive(false);
        }

        if (joinedPanel != null)
        {
            joinedPanel.SetActive(joined);
        }

        if (operationsGroup != null)
        {
            operationsGroup.SetActive(false);
        }

        SetConnectionButtonsInteractable(false);

        if (openLoginButton != null)
        {
            openLoginButton.interactable = !joined && loginPanel != null;
        }

        if (spawnButton != null)
        {
            spawnButton.interactable = joined && bottleSpawner != null;
        }

        if (despawnButton != null)
        {
            despawnButton.interactable = joined && bottleSpawner != null && bottleSpawner.SharedNetworkBottleCount > 0;
        }

        if (leaveRoomButton != null)
        {
            leaveRoomButton.interactable = joined;
        }

        string room = bootstrap != null ? bootstrap.DebugRoomName : "MissingBootstrap";
        string joinState = bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap";
        int activePlayers = bootstrap != null ? bootstrap.ActivePlayersCount : 0;
        SharedUserRole currentRole = bootstrap != null ? bootstrap.DebugCurrentRole : role;
        string headerState = joined ? "Joined" : ResolveDisconnectedHeaderState(joinState);
        SetStatus("Status: " + headerState);

        if (disconnectedStatusText != null)
        {
            disconnectedStatusText.text = "Status: " + (joined ? "Joined" : "Not Connected");
        }

        if (activePlayersText != null)
        {
            activePlayersText.text = "Room: " + room
                + "\nPlayers: " + activePlayers
                + "\nRole: " + currentRole;
        }

        if (protocolText != null)
        {
            protocolText.text = "Protocol: " + (bootstrap != null ? bootstrap.DebugProtocol : "Unavailable");
        }

        if (fixedRegionText != null)
        {
            fixedRegionText.text = "Fixed Region: " + (bootstrap != null ? bootstrap.DebugFixedRegion : "Unavailable");
        }

        string error = bootstrap != null ? bootstrap.LastError : "Photon bootstrap missing.";
        bool hasError = !joined && !string.IsNullOrWhiteSpace(error);
        if (errorPanel != null)
        {
            errorPanel.SetActive(hasError);
        }

        if (retryButton != null)
        {
            retryButton.interactable = hasError && loginPanel != null;
        }

        if (!string.IsNullOrEmpty(error) || force)
        {
            SetError(error);
        }

        if (debugToggleLabel != null)
        {
            bool debugOn = debugPanel != null && debugPanel.enableDebugPanel;
            debugToggleLabel.text = debugOn ? "DebugPanel: ON" : "DebugPanel: OFF";
        }
    }

    private static string ResolveDisconnectedHeaderState(string joinState)
    {
        if (string.IsNullOrWhiteSpace(joinState) || joinState == "NotStarted")
        {
            return "Not Connected";
        }

        if (joinState.IndexOf("Fail", StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Missing", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Failed";
        }

        if (joinState.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Join", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Connecting";
        }

        return "Not Connected";
    }

    private void SetConnectionButtonsInteractable(bool interactable)
    {
        if (startButton != null)
        {
            startButton.interactable = interactable;
        }

        if (retryButton != null)
        {
            retryButton.interactable = interactable;
        }
    }

    private void RefreshChoiceLabels()
    {
        if (hostModeLabel != null)
        {
            hostModeLabel.text = isHostLikeUser ? "Host-like" : "Client-like";
        }

        if (deviceTypeLabel != null)
        {
            deviceTypeLabel.text = "DeviceType: " + deviceType;
        }

        if (roleLabel != null)
        {
            roleLabel.text = "Role: " + role;
        }

        if (robotTargetLabel != null)
        {
            robotTargetLabel.text = "RobotTarget: " + robotTarget;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    private void SetError(string message)
    {
        if (errorText != null)
        {
            errorText.text = "Last Error: " + (string.IsNullOrWhiteSpace(message) ? "None" : message);
        }
    }

    private T FindChildComponent<T>(string objectName) where T : Component
    {
        GameObject child = FindChild(panelRoot.transform, objectName);
        return child != null ? child.GetComponent<T>() : null;
    }

    private TMP_Text FindChildLabel(string buttonName)
    {
        GameObject buttonObject = FindChild(panelRoot.transform, buttonName);
        if (buttonObject == null)
        {
            return null;
        }

        Transform label = buttonObject.transform.Find("Label");
        return label != null ? label.GetComponent<TMP_Text>() : buttonObject.GetComponentInChildren<TMP_Text>(true);
    }

    private static GameObject FindChild(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == objectName)
        {
            return root.gameObject;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            GameObject found = FindChild(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static T CycleEnum<T>(T current) where T : struct, Enum
    {
        T[] values = (T[])Enum.GetValues(typeof(T));
        if (values.Length == 0)
        {
            return current;
        }

        int index = Array.IndexOf(values, current);
        return values[(index + 1 + values.Length) % values.Length];
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

    private static TMP_Text CreateLabel(Transform parent, string objectName, string text, int fontSize, Vector2 position, Vector2 size, FontStyles fontStyle)
    {
        GameObject labelObject = CreateUiObject(objectName, parent, typeof(TextMeshProUGUI));
        ConfigureCenteredRect(labelObject.GetComponent<RectTransform>(), position, size);

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.fontStyle = fontStyle;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableWordWrapping = true;
        return label;
    }

    private static TMP_InputField CreateInput(Transform parent, string objectName, string placeholder, Vector2 position)
    {
        GameObject inputObject = CreateUiObject(objectName, parent, typeof(Image), typeof(TMP_InputField));
        ConfigureCenteredRect(inputObject.GetComponent<RectTransform>(), position, new Vector2(246f, 34f));

        Image image = inputObject.GetComponent<Image>();
        image.color = new Color(0.08f, 0.1f, 0.12f, 0.95f);

        GameObject textArea = CreateUiObject("Text Area", inputObject.transform, typeof(RectMask2D));
        RectTransform textAreaRect = textArea.GetComponent<RectTransform>();
        textAreaRect.anchorMin = Vector2.zero;
        textAreaRect.anchorMax = Vector2.one;
        textAreaRect.offsetMin = new Vector2(8f, 2f);
        textAreaRect.offsetMax = new Vector2(-8f, -2f);

        TMP_Text placeholderText = CreateLabel(textArea.transform, "Placeholder", placeholder, 14, Vector2.zero, new Vector2(230f, 28f), FontStyles.Italic);
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.color = new Color(1f, 1f, 1f, 0.45f);

        TMP_Text text = CreateLabel(textArea.transform, "Text", string.Empty, 14, Vector2.zero, new Vector2(230f, 28f), FontStyles.Normal);
        text.alignment = TextAlignmentOptions.Left;
        text.color = Color.white;

        TMP_InputField input = inputObject.GetComponent<TMP_InputField>();
        input.targetGraphic = image;
        input.textViewport = textAreaRect;
        input.placeholder = placeholderText;
        input.textComponent = text;
        input.text = string.Empty;
        return input;
    }

    private static Button CreateButton(Transform parent, string objectName, string text, Vector2 position, Vector2 size, out TMP_Text label)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent, typeof(Image), typeof(Button));
        ConfigureCenteredRect(buttonObject.GetComponent<RectTransform>(), position, size);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.06f, 0.31f, 0.34f, 0.98f);

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.08f, 0.46f, 0.5f, 1f);
        colors.pressedColor = new Color(0.02f, 0.22f, 0.24f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        label = CreateLabel(buttonObject.transform, "Label", text, 13, Vector2.zero, size - new Vector2(8f, 6f), FontStyles.Bold);
        return button;
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>(true);
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("PhotonSharedMRMenuEventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
    }

    private static void DestroyDuplicatePanels(RectTransform menuRoot)
    {
        GameObject first = null;
        for (int i = menuRoot.childCount - 1; i >= 0; i--)
        {
            Transform child = menuRoot.GetChild(i);
            if (child.name != PanelObjectName)
            {
                continue;
            }

            if (first == null)
            {
                first = child.gameObject;
            }
            else
            {
                DestroyObject(child.gameObject);
            }
        }
    }

    private static void DestroyObject(GameObject obj)
    {
        if (obj == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(obj);
        }
        else
        {
            DestroyImmediate(obj);
        }
    }
}
