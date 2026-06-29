using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

[DisallowMultipleComponent]
public class PhotonSharedMRLoginPanel : MonoBehaviour
{
    private const string CanvasObjectName = "StartupPhotonLoginPanelCanvas";
    private const string PanelObjectName = "StartupPhotonLoginPanel";

    [Header("Session")]
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public RoleBasedInfoFilter roleFilter;
    public PhotonSharedMRSessionSettings defaultSettings = PhotonSharedMRSessionSettings.CreateDefault();
    public bool suppressBootstrapAutoJoin = true;
    public bool hidePanelAfterStart = true;
    public bool enableRuntimePanel = true;
    public bool showOnStart = true;
    public bool reopenOnLeaveOrDisconnect = true;
    public bool hideRobotSelectionForPcObserver = true;

    [Header("Panel Placement")]
    public float spawnDistance = 0.85f;
    public float verticalOffset = -0.08f;
    public float panelScale = 0.0015f;
    public float loginDistanceFromHead = 0.90f;
    public float loginHorizontalOffset = 0.00f;
    public float loginVerticalOffset = -0.12f;
    public float loginScale = 0.001f;
    public bool faceCameraOnOpen = true;

    [Header("UI References")]
    public GameObject panelRoot;
    public TMP_InputField userNameInput;
    public TMP_Dropdown hostModeDropdown;
    public TMP_Dropdown deviceTypeDropdown;
    public TMP_Dropdown roleDropdown;
    public TMP_Dropdown robotTargetDropdown;
    public TMP_InputField roomNameInput;
    public Button amirRobotButton;
    public Button roverRobotButton;
    public Button droneRobotButton;
    public Button observerRobotButton;
    public Button startButton;
    public Button retryButton;
    public TMP_Text selectionText;
    public TMP_Text protocolText;
    public TMP_Text fixedRegionText;
    public TMP_Text statusText;
    public TMP_Text errorText;

    private bool uiInitialized;
    private bool startRequested;
    private bool wasJoined;
    private bool hasSelectedRobot;
    private SharedMRRobotTarget selectedRobotTarget = SharedMRRobotTarget.Amir;
    private float nextStatusRefreshTime;

    private void Awake()
    {
        ResolveSceneReferences();
        if (suppressBootstrapAutoJoin && bootstrap != null)
        {
            bootstrap.autoJoinOnStart = false;
        }

        if (enableRuntimePanel)
        {
            EnsureUi();
            PopulateUi(defaultSettings);
            SetPanelVisible(false);
            RefreshStatusText(true);
        }
    }

    private void Start()
    {
        if (enableRuntimePanel && showOnStart && !IsJoined())
        {
            OpenLoginPanel();
        }
    }

    private void Update()
    {
        if (!enableRuntimePanel)
        {
            return;
        }

        if (Time.unscaledTime >= nextStatusRefreshTime)
        {
            RefreshStatusText(false);
            nextStatusRefreshTime = Time.unscaledTime + 0.2f;
        }

        bool joined = IsJoined();
        if (joined)
        {
            wasJoined = true;
            return;
        }

        if (wasJoined && reopenOnLeaveOrDisconnect)
        {
            wasJoined = false;
            ResetStartRequest();
            OpenLoginPanel();
        }
    }

    public void StartSessionFromUi()
    {
        PhotonSharedMRSessionSettings settings = CollectSettingsFromUi();
        if (!IsPcAutoObserverRuntime() && !hasSelectedRobot)
        {
            SetError("操作ロボットを選択してください。");
            RefreshSelectionVisuals();
            return;
        }

        _ = StartSessionWithSettings(settings);
    }

    public void RetrySessionFromUi()
    {
        if (IsJoined())
        {
            return;
        }

        startRequested = false;
        StartSessionFromUi();
    }

    public async Task StartSessionWithSettings(PhotonSharedMRSessionSettings settings)
    {
        EnsureBootstrap(nameof(StartSessionWithSettings), false);
        string joinStatus = bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap";
        bool runnerIsRunning = bootstrap != null && bootstrap.RunnerIsRunning;
        bool calibrationInProgress = PhotonSharedMRCalibrationGuard.CalibrationInProgress;
        LogStartSessionCalled(joinStatus, runnerIsRunning, calibrationInProgress);

        if (runnerIsRunning && IsJoinedStatus(joinStatus))
        {
            wasJoined = true;
            startRequested = false;
            SetStartInteractable(false);
            RefreshStatusText(true);
            if (enableRuntimePanel && hidePanelAfterStart)
            {
                CloseLoginPanel();
            }

            return;
        }

        if (calibrationInProgress)
        {
            SetStatus("Connection Status: CalibrationInProgress");
            SetError("Start session blocked during calibration.");
            SetStartInteractable(true);
            startRequested = false;
            return;
        }

        if (startRequested)
        {
            return;
        }

        startRequested = true;
        settings = BuildFixedSettings(settings);
        settings.Sanitize();
        defaultSettings = settings.Clone();

        SetStatus("Connection Status: Joining " + settings.roomName + " ...");
        SetError(string.Empty);
        SetStartInteractable(false);

        if (roleFilter != null)
        {
            roleFilter.SetManualRole(settings.role);
        }

        if (EnsureBootstrap(nameof(StartSessionWithSettings), true) == null)
        {
            SetStatus("Connection Status: MissingBootstrap");
            SetError("Photon bootstrap is missing. source=PhotonSharedMRLoginPanel.StartSessionWithSettings");
            SetStartInteractable(true);
            startRequested = false;
            Debug.LogError("[PhotonSharedMRLoginPanel] Photon bootstrap is missing"
                + " method=" + nameof(StartSessionWithSettings)
                + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress);
            return;
        }

        await bootstrap.StartSharedRoom(settings);

        if (bootstrap.IsRunning)
        {
            wasJoined = true;
            RefreshStatusText(true);
            if (enableRuntimePanel && hidePanelAfterStart)
            {
                CloseLoginPanel();
            }
        }
        else
        {
            SetStatus("Connection Status: Join failed");
            SetError(string.IsNullOrWhiteSpace(bootstrap.LastError) ? "Join failed. See Console." : bootstrap.LastError);
            SetStartInteractable(true);
            startRequested = false;
            if (enableRuntimePanel)
            {
                SetPanelVisible(true);
            }
        }
    }

    public void EnsurePanel()
    {
        if (!enableRuntimePanel)
        {
            return;
        }

        ResolveSceneReferences();
        EnsureUi();
        PopulateUi(defaultSettings);
        RefreshStatusText(true);
    }

    public void OpenLoginPanel()
    {
        enableRuntimePanel = true;
        EnsurePanel();
        SetPanelVisible(true);
        ApplyOpenPose(Camera.main != null ? Camera.main.transform : null);
        SetStartInteractable(!startRequested && !IsJoined());
        RefreshStatusText(true);
    }

    public void OpenLoginPanelAtHead(Transform head)
    {
        enableRuntimePanel = true;
        EnsurePanel();
        SetPanelVisible(true);
        ApplyOpenPose(head);
        SetStartInteractable(!startRequested && !IsJoined());
        RefreshStatusText(true);
    }

    public void CloseLoginPanel()
    {
        SetPanelVisible(false);
    }

    public void SetPanelVisible(bool visible)
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(visible);
            Canvas canvas = panelRoot.GetComponent<Canvas>();
            if (canvas != null && Camera.main != null)
            {
                canvas.worldCamera = Camera.main;
            }
        }
    }

    public void ResetStartRequest()
    {
        startRequested = false;
        SetStartInteractable(true);
    }

    public PhotonSharedMRSessionSettings CollectSettingsFromUi()
    {
        return BuildFixedSettings(defaultSettings);
    }

    public void SelectAmirRobot()
    {
        SelectRobot(SharedMRRobotTarget.Amir);
    }

    public void SelectRoverRobot()
    {
        SelectRobot(SharedMRRobotTarget.Rover);
    }

    public void SelectDroneRobot()
    {
        SelectRobot(SharedMRRobotTarget.Drone);
    }

    public void SelectObserverRobot()
    {
        SelectRobot(SharedMRRobotTarget.Observer);
    }

    private void SelectRobot(SharedMRRobotTarget robotTarget)
    {
        selectedRobotTarget = robotTarget;
        hasSelectedRobot = true;
        defaultSettings = BuildFixedSettings(defaultSettings);
        if (roleFilter != null)
        {
            roleFilter.SetManualRole(defaultSettings.role);
        }

        RefreshSelectionVisuals();
        RefreshStatusText(true);
    }

    private PhotonSharedMRSessionSettings BuildFixedSettings(PhotonSharedMRSessionSettings seed)
    {
        PhotonSharedMRSessionSettings settings = seed != null
            ? seed.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();

        ShareDeviceType deviceType = DetectDeviceType();
        if (PhotonSharedMRSessionSettings.IsPcObserverDevice(deviceType))
        {
            return PhotonSharedMRSessionSettings.CreatePcObserverDefaults(deviceType);
        }

        settings.roomName = PhotonSharedMRSessionSettings.DefaultRoomName;
        settings.userName = PhotonSharedMRSessionSettings.BuildRobotDisplayName(
            selectedRobotTarget,
            ResolveRoleForRobot(selectedRobotTarget));
        settings.isHostLikeUser = true;
        settings.deviceType = deviceType;
        settings.robotTarget = selectedRobotTarget;
        settings.role = ResolveRoleForRobot(selectedRobotTarget);
        settings.Sanitize();
        return settings;
    }

    private static SharedUserRole ResolveRoleForRobot(SharedMRRobotTarget robotTarget)
    {
        switch (robotTarget)
        {
            case SharedMRRobotTarget.Drone:
                return SharedUserRole.Scout;
            case SharedMRRobotTarget.Observer:
                return SharedUserRole.Supervisor;
            case SharedMRRobotTarget.Amir:
            case SharedMRRobotTarget.Rover:
            default:
                return SharedUserRole.ManipulatorOperator;
        }
    }

    private static ShareDeviceType DetectDeviceType()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return ShareDeviceType.QuestStandalone;
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return ShareDeviceType.PC;
#else
        return ShareDeviceType.PCEditor;
#endif
    }

    private static bool IsPcAutoObserverRuntime()
    {
        return PhotonSharedMRSessionSettings.IsPcObserverDevice(DetectDeviceType());
    }

    private void ResolveSceneReferences()
    {
        if (bootstrap == null)
        {
            EnsureBootstrap(nameof(ResolveSceneReferences), false);
        }

        EnsureBootstrap(nameof(ResolveSceneReferences), false);

        if (roleFilter == null)
        {
            roleFilter = FindObjectOfType<RoleBasedInfoFilter>(true);
        }
    }

    private void EnsureUi()
    {
        if (uiInitialized)
        {
            ApplyPcObserverUiState();
            return;
        }

        if (panelRoot == null)
        {
            Transform existing = transform.Find(CanvasObjectName);
            panelRoot = existing != null ? existing.gameObject : CreatePanelUi();
        }

        ResolveUiReferences();
        if (!HasRequiredUi())
        {
            DestroyObject(panelRoot);
            panelRoot = CreatePanelUi();
            ResolveUiReferences();
        }

        EnsureEventSystem();
        WireStartButton();
        uiInitialized = true;
        ApplyPcObserverUiState();
    }

    private GameObject CreatePanelUi()
    {
        GameObject canvasObject = new GameObject(
            CanvasObjectName,
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
        canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, loginScale);

        GameObject panel = CreateUiObject(PanelObjectName, canvasObject.transform, typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.03f, 0.06f, 0.08f, 0.94f);

        CreateLabel(panel.transform, "Title", "Photon Shared MR", 30, new Vector2(0f, 248f), new Vector2(460f, 48f), FontStyles.Bold);
        selectionText = CreateLabel(panel.transform, "RobotSelectionLabel", "操作ロボットを選択", 22, new Vector2(0f, 194f), new Vector2(460f, 36f), FontStyles.Normal);

        amirRobotButton = CreateButton(panel.transform, "AmirButton", "AMIR", new Vector2(-118f, 128f));
        roverRobotButton = CreateButton(panel.transform, "RoverButton", "Rover", new Vector2(118f, 128f));
        droneRobotButton = CreateButton(panel.transform, "DroneButton", "Drone", new Vector2(-118f, 58f));
        observerRobotButton = CreateButton(panel.transform, "ObserverButton", "Observer", new Vector2(118f, 58f));

        protocolText = CreateLabel(panel.transform, "ProtocolText", "Room: SHARE-MR-Room", 16, new Vector2(0f, 2f), new Vector2(430f, 24f), FontStyles.Normal);
        fixedRegionText = CreateLabel(panel.transform, "FixedRegionText", "Mode: AutoHostOrClient", 16, new Vector2(0f, -26f), new Vector2(430f, 24f), FontStyles.Normal);
        startButton = CreateButton(panel.transform, "StartButton", "共有空間に参加", new Vector2(0f, -98f));
        statusText = CreateLabel(panel.transform, "StatusText", "Connection Status: NotStarted", 17, new Vector2(0f, -176f), new Vector2(460f, 58f), FontStyles.Normal);
        errorText = CreateLabel(panel.transform, "LastErrorText", "Last Error: None", 16, new Vector2(0f, -250f), new Vector2(460f, 58f), FontStyles.Normal);
        errorText.color = new Color(1f, 0.55f, 0.45f, 1f);

        return canvasObject;
    }

    private void PopulateUi(PhotonSharedMRSessionSettings settings)
    {
        settings ??= PhotonSharedMRSessionSettings.CreateDefault();
        settings.Sanitize();
        selectedRobotTarget = settings.robotTarget;
        if (IsPcAutoObserverRuntime())
        {
            selectedRobotTarget = SharedMRRobotTarget.Observer;
            hasSelectedRobot = true;
        }

        RefreshSelectionVisuals();
    }

    private void ResolveUiReferences()
    {
        if (panelRoot == null)
        {
            return;
        }

        userNameInput = FindChildComponent<TMP_InputField>("UserNameInput");
        hostModeDropdown = FindChildComponent<TMP_Dropdown>("HostModeDropdown");
        deviceTypeDropdown = FindChildComponent<TMP_Dropdown>("DeviceTypeDropdown");
        roleDropdown = FindChildComponent<TMP_Dropdown>("RoleDropdown");
        robotTargetDropdown = FindChildComponent<TMP_Dropdown>("RobotTargetDropdown");
        roomNameInput = FindChildComponent<TMP_InputField>("RoomNameInput");
        amirRobotButton = FindChildComponent<Button>("AmirButton");
        roverRobotButton = FindChildComponent<Button>("RoverButton");
        droneRobotButton = FindChildComponent<Button>("DroneButton");
        observerRobotButton = FindChildComponent<Button>("ObserverButton");
        startButton = FindChildComponent<Button>("StartButton");
        retryButton = FindChildComponent<Button>("RetryButton");
        selectionText = FindChildComponent<TMP_Text>("RobotSelectionLabel");
        protocolText = FindChildComponent<TMP_Text>("ProtocolText");
        fixedRegionText = FindChildComponent<TMP_Text>("FixedRegionText");
        statusText = FindChildComponent<TMP_Text>("StatusText");
        errorText = FindChildComponent<TMP_Text>("LastErrorText");
    }

    private bool HasRequiredUi()
    {
        return panelRoot != null
            && userNameInput == null
            && hostModeDropdown == null
            && deviceTypeDropdown == null
            && roleDropdown == null
            && robotTargetDropdown == null
            && roomNameInput == null
            && amirRobotButton != null
            && roverRobotButton != null
            && droneRobotButton != null
            && observerRobotButton != null
            && startButton != null
            && selectionText != null
            && protocolText != null
            && fixedRegionText != null
            && statusText != null
            && errorText != null;
    }

    private void WireStartButton()
    {
        WireButton(startButton, StartSessionFromUi, nameof(StartSessionFromUi));
        WireButton(amirRobotButton, SelectAmirRobot, nameof(SelectAmirRobot));
        WireButton(roverRobotButton, SelectRoverRobot, nameof(SelectRoverRobot));
        WireButton(droneRobotButton, SelectDroneRobot, nameof(SelectDroneRobot));
        WireButton(observerRobotButton, SelectObserverRobot, nameof(SelectObserverRobot));
        WireButton(retryButton, RetrySessionFromUi, nameof(RetrySessionFromUi));

        RefreshSelectionVisuals();
    }

    private void WireButton(Button button, UnityAction action, string methodName)
    {
        if (button == null || action == null)
        {
            return;
        }

        button.onClick.RemoveListener(action);
        if (!HasPersistentListener(button.onClick, this, methodName))
        {
            button.onClick.AddListener(action);
        }
    }

    private static bool HasPersistentListener(UnityEventBase unityEvent, UnityEngine.Object target, string methodName)
    {
        if (unityEvent == null || target == null || string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == target
                && unityEvent.GetPersistentMethodName(i) == methodName)
            {
                return true;
            }
        }

        return false;
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }

        Debug.Log("[PhotonSharedMRLoginPanel] " + message);
    }

    private void SetError(string message)
    {
        if (errorText != null)
        {
            errorText.text = "Last Error: " + (string.IsNullOrWhiteSpace(message) ? "None" : message);
        }
    }

    private void SetStartInteractable(bool interactable)
    {
        if (startButton != null)
        {
            startButton.interactable = interactable && (hasSelectedRobot || IsPcAutoObserverRuntime());
        }

        if (retryButton != null)
        {
            retryButton.interactable = interactable;
        }
    }

    private void RefreshStatusText(bool force)
    {
        if (!force && panelRoot != null && !panelRoot.activeSelf)
        {
            return;
        }

        EnsureBootstrap(nameof(RefreshStatusText), false);

        if (protocolText != null)
        {
            protocolText.text = "Room: " + PhotonSharedMRSessionSettings.DefaultRoomName;
        }

        if (fixedRegionText != null)
        {
            fixedRegionText.text = "Mode: AutoHostOrClient";
        }

        string room = PhotonSharedMRSessionSettings.DefaultRoomName;
        string joinState = bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap";
        bool joined = IsJoined();
        bool pcAutoObserver = IsPcAutoObserverRuntime();
        if (statusText != null)
        {
            statusText.text = "Connection Status: " + (joined ? "Joined" : joinState)
                + "\nRoomName: " + room
                + "\nRobot: " + (pcAutoObserver ? SharedMRRobotTarget.Observer.ToString() : (hasSelectedRobot ? selectedRobotTarget.ToString() : "Not Selected"));
        }

        if (force || joined || !string.IsNullOrWhiteSpace(bootstrap != null ? bootstrap.LastError : null))
        {
            SetError(bootstrap != null ? bootstrap.LastError : "Photon bootstrap missing.");
        }

        RefreshSelectionVisuals();
    }

    private void RefreshSelectionVisuals()
    {
        ApplyPcObserverUiState();
        RefreshRobotButtonVisual(amirRobotButton, SharedMRRobotTarget.Amir);
        RefreshRobotButtonVisual(roverRobotButton, SharedMRRobotTarget.Rover);
        RefreshRobotButtonVisual(droneRobotButton, SharedMRRobotTarget.Drone);
        RefreshRobotButtonVisual(observerRobotButton, SharedMRRobotTarget.Observer);

        if (selectionText != null)
        {
            selectionText.text = hasSelectedRobot
                ? "操作ロボット: " + selectedRobotTarget
                : "操作ロボットを選択";
        }

        if (selectionText != null && IsPcAutoObserverRuntime())
        {
            selectionText.text = "Observer fixed";
        }

        if (startButton != null)
        {
            startButton.interactable = !startRequested && !IsJoined() && (hasSelectedRobot || IsPcAutoObserverRuntime());
        }
    }

    private void ApplyPcObserverUiState()
    {
        bool pcAutoObserver = IsPcAutoObserverRuntime();
        if (pcAutoObserver)
        {
            selectedRobotTarget = SharedMRRobotTarget.Observer;
            hasSelectedRobot = true;
        }

        if (!hideRobotSelectionForPcObserver)
        {
            return;
        }

        SetButtonVisible(amirRobotButton, !pcAutoObserver);
        SetButtonVisible(roverRobotButton, !pcAutoObserver);
        SetButtonVisible(droneRobotButton, !pcAutoObserver);
        SetButtonVisible(observerRobotButton, !pcAutoObserver);
    }

    private static void SetButtonVisible(Button button, bool visible)
    {
        if (button != null)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private void RefreshRobotButtonVisual(Button button, SharedMRRobotTarget robotTarget)
    {
        if (button == null)
        {
            return;
        }

        bool selected = hasSelectedRobot && selectedRobotTarget == robotTarget;
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            image.color = selected
                ? new Color(0.10f, 0.58f, 0.42f, 1f)
                : new Color(0.05f, 0.34f, 0.42f, 0.98f);
        }

        ColorBlock colors = button.colors;
        colors.normalColor = image != null ? image.color : colors.normalColor;
        colors.highlightedColor = selected
            ? new Color(0.16f, 0.72f, 0.52f, 1f)
            : new Color(0.08f, 0.48f, 0.58f, 1f);
        colors.pressedColor = selected
            ? new Color(0.06f, 0.42f, 0.30f, 1f)
            : new Color(0.02f, 0.25f, 0.32f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;
    }

    private void ApplyOpenPose(Transform head)
    {
        if (panelRoot == null)
        {
            return;
        }

        if (head == null && Camera.main != null)
        {
            head = Camera.main.transform;
        }

        Transform panelTransform = panelRoot.transform;
        if (head != null)
        {
            Vector3 forward = head.forward.sqrMagnitude > 0.0001f ? head.forward.normalized : Vector3.forward;
            Vector3 right = head.right.sqrMagnitude > 0.0001f ? head.right.normalized : Vector3.right;
            Vector3 up = head.up.sqrMagnitude > 0.0001f ? head.up.normalized : Vector3.up;
            Vector3 position = head.position
                + forward * Mathf.Max(0.05f, loginDistanceFromHead)
                + right * loginHorizontalOffset
                + up * loginVerticalOffset;

            Quaternion rotation = panelTransform.rotation;
            if (faceCameraOnOpen)
            {
                Vector3 facing = position - head.position;
                if (facing.sqrMagnitude < 0.0001f)
                {
                    facing = forward;
                }

                rotation = Quaternion.LookRotation(facing, Vector3.up);
            }

            panelTransform.SetPositionAndRotation(position, rotation);
        }

        panelTransform.localScale = Vector3.one * Mathf.Max(0.00001f, loginScale);
    }

    private bool IsJoined()
    {
        EnsureBootstrap(nameof(IsJoined), false);
        return bootstrap != null && bootstrap.IsRunning;
    }

    private static bool IsJoinedStatus(string joinStatus)
    {
        return string.Equals(joinStatus, "Joined", StringComparison.Ordinal)
            || string.Equals(joinStatus, "AlreadyRunning", StringComparison.Ordinal);
    }

    private void LogStartSessionCalled(string joinStatus, bool runnerIsRunning, bool calibrationInProgress)
    {
        string stackTrace = StackTraceUtility.ExtractStackTrace();
        string message = "[PhotonSharedMRLoginPanel] PHOTON_START_SESSION_CALLED"
            + " joinStatus=" + (string.IsNullOrWhiteSpace(joinStatus) ? "Unknown" : joinStatus)
            + " runnerIsRunning=" + runnerIsRunning
            + " calibrationInProgress=" + calibrationInProgress
            + " stackTrace=" + FormatStackTraceForLog(stackTrace);

        if (calibrationInProgress)
        {
            Debug.LogWarning(message);
            Debug.LogWarning("[PhotonSharedMRLoginPanel] PHOTON_START_SESSION_UNEXPECTED_DURING_CALIBRATION");
        }
        else
        {
            Debug.Log(message);
        }
    }

    private static string FormatStackTraceForLog(string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace))
        {
            return "Unavailable";
        }

        return stackTrace
            .Replace("\r\n", " | ")
            .Replace('\n', '|')
            .Replace('\r', '|');
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private T FindChildComponent<T>(string objectName) where T : Component
    {
        GameObject child = FindChild(panelRoot != null ? panelRoot.transform : null, objectName);
        return child != null ? child.GetComponent<T>() : null;
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
