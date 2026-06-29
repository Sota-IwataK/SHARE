using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public class PhotonSharedBottleSpawner : MonoBehaviour
{
    [Header("Shared Bottle Spawn")]
    public bool enableSharedBottleSpawn = true;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public GameObject networkBottlePrefab;
    public Transform spawnAnchor;
    public float spawnDistance = 0.65f;
    public float spawnVerticalOffset = -0.08f;
    public int maxSharedBottleCount = 8;

    [Header("Quest / Editor Spawn Controls")]
    public bool enableSpawnControls = true;
    public float controlsDistance = 0.95f;
    public float controlsVerticalOffset = -0.42f;
    public float controlsScale = 0.0012f;
    public bool allowKeyboardShortcutsInEditor = true;
    public KeyCode spawnKey = KeyCode.B;
    public KeyCode despawnKey = KeyCode.N;

    [Header("Runtime UI References")]
    public GameObject controlsRoot;
    public Button spawnButton;
    public Button despawnButton;
    public TMP_Text statusText;

    private readonly HashSet<string> observedBottleIds = new HashSet<string>();
    private readonly HashSet<string> localBottleLogIds = new HashSet<string>();
    private readonly HashSet<string> remoteBottleLogIds = new HashSet<string>();
    private int localSpawnRequestCount;
    private int remoteSpawnObservedCount;
    private string lastSpawnedBottleNetworkId = "None";
    private string lastSpawnError = "None";

#if FUSION_WEAVER && FUSION2
    private NetworkObject pendingDespawnObject;
#endif

    public int SharedNetworkBottleCount => CountSharedNetworkBottles();
    public int LocalSpawnRequestCount => localSpawnRequestCount;
    public int RemoteSpawnObservedCount => remoteSpawnObservedCount;
    public string LastSpawnedBottleNetworkId => lastSpawnedBottleNetworkId;
    public string LastSpawnError => lastSpawnError;

    private void Awake()
    {
        ResolveReferences();
        EnsureControls();
        RefreshStatusText();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureControls();
        WireButtons();
    }

    private void OnDisable()
    {
        UnwireButtons();
    }

    private void Update()
    {
        ResolveReferences();
        ObserveSharedNetworkBottles();
        ProcessPendingDespawn();
        HandleKeyboardShortcuts();
        UpdateControlsPose();
        RefreshStatusText();
    }

    public void RequestSpawnInFrontOfHmd()
    {
        SpawnSharedBottle("PhotonSharedBottleSpawner");
    }

    public void SpawnSharedBottle(string source)
    {
        string resolvedSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
        if (!CanSpawnSharedBottle(out string reason))
        {
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE local"
                + " source=" + resolvedSource
                + " reason=" + reason);
            FailSpawn(reason);
            return;
        }

        if (!TryResolveSpawnPose(out Vector3 position, out Quaternion rotation))
        {
            localSpawnRequestCount++;
            FailSpawn("SpawnAnchorMissing");
            return;
        }

        SpawnSharedBottleAtPose(position, rotation, resolvedSource);
    }

    public NetworkedSharedSceneObject SpawnSharedBottleAtPose(Vector3 position, Quaternion rotation, string source)
    {
        string resolvedSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
        if (!CanSpawnSharedBottle(out string reason))
        {
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE local"
                + " source=" + resolvedSource
                + " reason=" + reason);
            FailSpawn(reason);
            return null;
        }

        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_MODE shared source=" + resolvedSource);
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST source=" + resolvedSource);
        return RequestSpawnInternal(position, rotation, resolvedSource);
    }

    public bool CanSpawnSharedBottle(out string reason)
    {
        ResolveReferences();
        EnsureBootstrap(nameof(CanSpawnSharedBottle), false);

        if (!enableSharedBottleSpawn)
        {
            reason = "SpawnerDisabled";
            return false;
        }

        if (bootstrap == null)
        {
            reason = "MissingBootstrap";
            return false;
        }

        if (IsDisconnectOrLeaveStatus(bootstrap.LastJoinStatus))
        {
            reason = bootstrap.LastJoinStatus;
            return false;
        }

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null)
        {
            reason = "RunnerMissing";
            return false;
        }

        if (!runner.IsRunning)
        {
            reason = "RunnerNotRunning";
            return false;
        }

        if (!IsJoinedStatus(bootstrap.LastJoinStatus))
        {
            reason = "PhotonNotJoined";
            return false;
        }

        if (networkBottlePrefab == null)
        {
            reason = "MissingNetworkBottlePrefab";
            return false;
        }

        if (networkBottlePrefab.GetComponent<NetworkObject>() == null)
        {
            reason = "NetworkBottlePrefabMissingNetworkObject";
            return false;
        }

        reason = "Ready";
        return true;
#else
        reason = "FusionDisabled";
        return false;
#endif
    }

    public void RequestDespawnLastSharedBottle()
    {
#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            FailSpawn("RunnerNotRunningForDespawn");
            return;
        }

        NetworkedSharedSceneObject target = FindNewestSharedBottleWithNetworkObject();
        if (target == null || target.Object == null)
        {
            FailSpawn("NoSharedBottleToDespawn");
            return;
        }

        if (!target.HasStateAuthority)
        {
            pendingDespawnObject = target.Object;
            target.Object.RequestStateAuthority();
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_DESPAWN authorityRequested=True"
                + " networkId=" + FormatNetworkId(target.Object)
                + " player=" + runner.LocalPlayer);
            return;
        }

        DespawnNetworkBottle(runner, target.Object);
#else
        FailSpawn("FusionDisabledForDespawn");
#endif
    }

    public void RequestSpawn(Vector3 position, Quaternion rotation)
    {
        RequestSpawn(position, rotation, string.Empty);
    }

    public void RequestSpawn(Vector3 position, Quaternion rotation, string source)
    {
        RequestSpawnInternal(position, rotation, source);
    }

    private NetworkedSharedSceneObject RequestSpawnInternal(Vector3 position, Quaternion rotation, string source)
    {
        localSpawnRequestCount++;
        lastSpawnError = "None";
        string sourceSuffix = string.IsNullOrWhiteSpace(source) ? string.Empty : " source=" + source;

#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = ResolveRunner();
        string playerText = runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None";
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST"
            + sourceSuffix
            + " player=" + playerText
            + " position=" + FormatVector(position)
            + " rotation=" + FormatQuaternion(rotation));

        if (!enableSharedBottleSpawn)
        {
            FailSpawn("SpawnerDisabled");
            return null;
        }

        if (runner == null || !runner.IsRunning)
        {
            FailSpawn("RunnerNotRunning");
            return null;
        }

        if (networkBottlePrefab == null)
        {
            FailSpawn("MissingNetworkBottlePrefab");
            return null;
        }

        if (networkBottlePrefab.GetComponent<NetworkObject>() == null)
        {
            FailSpawn("NetworkBottlePrefabMissingNetworkObject");
            return null;
        }

        if (SharedNetworkBottleCount >= Mathf.Max(1, maxSharedBottleCount))
        {
            FailSpawn("MaxSharedBottleCountReached count=" + SharedNetworkBottleCount);
            return null;
        }

        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_AUTHORITY"
            + " player=" + runner.LocalPlayer
            + " mode=Shared"
            + " prefab=" + networkBottlePrefab.name);

        try
        {
            NetworkObject spawned = runner.Spawn(
                networkBottlePrefab,
                position,
                rotation,
                runner.LocalPlayer,
                (spawnRunner, obj) =>
                {
                    ConfigureSpawnedBottle(obj, spawnRunner.LocalPlayer, (float)spawnRunner.SimulationTime);
                });

            if (spawned == null)
            {
                FailSpawn("RunnerSpawnReturnedNull");
                return null;
            }

            RegisterObservedBottle(spawned, true);
            return spawned.GetComponent<NetworkedSharedSceneObject>();
        }
        catch (Exception ex)
        {
            FailSpawn(ex.GetType().Name + ": " + ex.Message);
            return null;
        }
#else
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REQUEST player=FusionDisabled"
            + " position=" + FormatVector(position)
            + " rotation=" + FormatQuaternion(rotation));
        FailSpawn("FusionDisabled");
        return null;
#endif
    }

    public NetworkedSharedSceneObject FindLatestSharedBottle()
    {
        return FindNewestSharedBottleWithNetworkObject();
    }

    private void ResolveReferences()
    {
        if (bootstrap == null)
        {
            EnsureBootstrap(nameof(ResolveReferences), false);
        }

        EnsureBootstrap(nameof(ResolveReferences), false);
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private static bool IsJoinedStatus(string status)
    {
        return string.Equals(status, "Joined", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "AlreadyRunning", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDisconnectOrLeaveStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.IndexOf("Leaving", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Leave", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Shutdown", StringComparison.OrdinalIgnoreCase) >= 0
            || status.IndexOf("Disconnect", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        Transform anchor = spawnAnchor;
        if (anchor == null && Camera.main != null)
        {
            anchor = Camera.main.transform;
        }

        if (anchor == null && bootstrap != null)
        {
            anchor = bootstrap.headSource;
        }

        if (anchor == null)
        {
            position = transform.position;
            rotation = transform.rotation;
            return false;
        }

        Vector3 forward = anchor.forward.sqrMagnitude > 0.0001f ? anchor.forward.normalized : Vector3.forward;
        position = anchor.position
            + forward * Mathf.Max(0.05f, spawnDistance)
            + Vector3.up * spawnVerticalOffset;
        rotation = Quaternion.Euler(0f, anchor.eulerAngles.y, 0f);
        return true;
    }

    private void HandleKeyboardShortcuts()
    {
#if UNITY_EDITOR || UNITY_STANDALONE
        if (!allowKeyboardShortcutsInEditor)
        {
            return;
        }

        if (Input.GetKeyDown(spawnKey))
        {
            RequestSpawnInFrontOfHmd();
        }
        else if (Input.GetKeyDown(despawnKey))
        {
            RequestDespawnLastSharedBottle();
        }
#endif
    }

    private void EnsureControls()
    {
        if (!enableSpawnControls)
        {
            if (controlsRoot != null)
            {
                controlsRoot.SetActive(false);
            }

            return;
        }

        if (controlsRoot == null)
        {
            controlsRoot = CreateControlsUi();
        }

        controlsRoot.SetActive(true);
        EnsureEventSystem();
        WireButtons();
    }

    private GameObject CreateControlsUi()
    {
        GameObject canvasObject = new GameObject(
            "SharedBottleSpawnerCanvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(TrackedDeviceGraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 92;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 12f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 190f);
        canvasRect.localScale = Vector3.one * Mathf.Max(0.0001f, controlsScale);

        GameObject panel = CreateUiObject("SharedBottleSpawnerPanel", canvasObject.transform, typeof(Image));
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = new Color(0.025f, 0.05f, 0.055f, 0.92f);

        spawnButton = CreateButton(panel.transform, "SpawnSharedBottleButton", "Spawn Shared Bottle", new Vector2(0f, 42f), new Vector2(330f, 52f));
        despawnButton = CreateButton(panel.transform, "DespawnSharedBottleButton", "Despawn Last", new Vector2(0f, -22f), new Vector2(330f, 44f));
        statusText = CreateLabel(panel.transform, "SharedBottleSpawnerStatus", "SharedBottle: 0", 16, new Vector2(0f, -76f), new Vector2(370f, 30f));

        return canvasObject;
    }

    private void WireButtons()
    {
        if (spawnButton != null)
        {
            spawnButton.onClick.RemoveListener(RequestSpawnInFrontOfHmd);
            spawnButton.onClick.AddListener(RequestSpawnInFrontOfHmd);
        }

        if (despawnButton != null)
        {
            despawnButton.onClick.RemoveListener(RequestDespawnLastSharedBottle);
            despawnButton.onClick.AddListener(RequestDespawnLastSharedBottle);
        }
    }

    private void UnwireButtons()
    {
        if (spawnButton != null)
        {
            spawnButton.onClick.RemoveListener(RequestSpawnInFrontOfHmd);
        }

        if (despawnButton != null)
        {
            despawnButton.onClick.RemoveListener(RequestDespawnLastSharedBottle);
        }
    }

    private void UpdateControlsPose()
    {
        if (!enableSpawnControls || controlsRoot == null || !controlsRoot.activeSelf)
        {
            return;
        }

        Transform target = spawnAnchor != null ? spawnAnchor : (Camera.main != null ? Camera.main.transform : null);
        if (target == null)
        {
            return;
        }

        Vector3 forward = target.forward.sqrMagnitude > 0.0001f ? target.forward.normalized : Vector3.forward;
        Transform controlsTransform = controlsRoot.transform;
        controlsTransform.position = target.position
            + forward * Mathf.Max(0.05f, controlsDistance)
            + Vector3.up * controlsVerticalOffset;
        controlsTransform.localScale = Vector3.one * Mathf.Max(0.0001f, controlsScale);

        Vector3 facing = controlsTransform.position - target.position;
        if (facing.sqrMagnitude < 0.0001f)
        {
            facing = forward;
        }

        controlsTransform.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    private void RefreshStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = "SharedNetworkBottleCount: " + SharedNetworkBottleCount
            + "  Last: " + lastSpawnedBottleNetworkId;
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

    private static Button CreateButton(Transform parent, string objectName, string text, Vector2 position, Vector2 size)
    {
        GameObject buttonObject = CreateUiObject(objectName, parent, typeof(Image), typeof(Button));
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, position, size);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.06f, 0.38f, 0.34f, 0.98f);

        Button button = buttonObject.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.08f, 0.52f, 0.46f, 1f);
        colors.pressedColor = new Color(0.02f, 0.26f, 0.24f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        CreateLabel(buttonObject.transform, "Label", text, 20, Vector2.zero, size - new Vector2(12f, 8f));
        return button;
    }

    private static TMP_Text CreateLabel(Transform parent, string objectName, string text, int fontSize, Vector2 position, Vector2 size)
    {
        GameObject labelObject = CreateUiObject(objectName, parent, typeof(TextMeshProUGUI));
        RectTransform rect = labelObject.GetComponent<RectTransform>();
        ConfigureCenteredRect(rect, position, size);

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        return label;
    }

    private static void ConfigureCenteredRect(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindObjectOfType<EventSystem>(true);
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("PhotonSharedMRSpawnEventSystem", typeof(EventSystem));
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }

        if (eventSystem.GetComponent<XRUIInputModule>() == null)
        {
            eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }
    }

    private int CountSharedNetworkBottles()
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        int count = 0;
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject != null && sharedObject.IsPhotonSharedNetworkBottle)
            {
                count++;
            }
        }

        return count;
    }

    private NetworkedSharedSceneObject FindNewestSharedBottleWithNetworkObject()
    {
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        NetworkedSharedSceneObject newest = null;
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle)
            {
                continue;
            }

#if FUSION_WEAVER && FUSION2
            if (sharedObject.Object == null || !sharedObject.Object.Id.IsValid)
            {
                continue;
            }

            newest = sharedObject;
#endif
        }

        return newest;
    }

    private void ObserveSharedNetworkBottles()
    {
#if FUSION_WEAVER && FUSION2
        NetworkedSharedSceneObject[] sharedObjects = FindObjectsOfType<NetworkedSharedSceneObject>(true);
        for (int i = 0; i < sharedObjects.Length; i++)
        {
            NetworkedSharedSceneObject sharedObject = sharedObjects[i];
            if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle || sharedObject.Object == null)
            {
                continue;
            }

            RegisterObservedBottle(sharedObject.Object, false);
        }
#endif
    }

    private void ProcessPendingDespawn()
    {
#if FUSION_WEAVER && FUSION2
        if (pendingDespawnObject == null)
        {
            return;
        }

        NetworkRunner runner = ResolveRunner();
        if (runner == null || !runner.IsRunning)
        {
            pendingDespawnObject = null;
            return;
        }

        if (!pendingDespawnObject.HasStateAuthority)
        {
            return;
        }

        DespawnNetworkBottle(runner, pendingDespawnObject);
        pendingDespawnObject = null;
#endif
    }

#if FUSION_WEAVER && FUSION2
    private NetworkRunner ResolveRunner()
    {
        if (EnsureBootstrap(nameof(ResolveRunner), false) == null)
        {
            return null;
        }

        return bootstrap.Runner;
    }

    private static void ConfigureSpawnedBottle(NetworkObject obj, PlayerRef spawnedBy, float spawnedAtRunnerTime)
    {
        NetworkedSharedSceneObject sharedObject = obj != null ? obj.GetComponent<NetworkedSharedSceneObject>() : null;
        if (sharedObject == null)
        {
            return;
        }

        sharedObject.objectKind = SharedNetworkObjectKind.Bottle;
        sharedObject.allowStateAuthorityGrab = true;
        sharedObject.allowMouseEditorGrab = true;
        sharedObject.syncPose = true;
        sharedObject.isPhotonSharedNetworkBottle = true;
        sharedObject.SetSharedSpawnMetadata(spawnedBy, spawnedAtRunnerTime);
    }

    private void RegisterObservedBottle(NetworkObject networkObject, bool fromLocalSpawnCall)
    {
        if (networkObject == null || !networkObject.Id.IsValid)
        {
            return;
        }

        NetworkedSharedSceneObject sharedObject = networkObject.GetComponent<NetworkedSharedSceneObject>();
        if (sharedObject == null || !sharedObject.IsPhotonSharedNetworkBottle)
        {
            return;
        }

        NetworkRunner runner = ResolveRunner();
        string networkId = FormatNetworkId(networkObject);
        observedBottleIds.Add(networkId);
        lastSpawnedBottleNetworkId = networkId;

        bool localAuthority = runner != null
            && runner.IsRunning
            && (networkObject.StateAuthority == runner.LocalPlayer || networkObject.InputAuthority == runner.LocalPlayer);

        if ((fromLocalSpawnCall || localAuthority) && localBottleLogIds.Add(networkId))
        {
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_LOCAL"
                + " networkId=" + networkId
                + " player=" + (runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None")
                + " spawnedBy=" + sharedObject.DebugSpawnedByPlayer
                + " spawnedAt=" + sharedObject.DebugSpawnedAtRunnerTime);
        }
        else if (!localAuthority && remoteBottleLogIds.Add(networkId))
        {
            remoteSpawnObservedCount++;
            Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_REMOTE"
                + " networkId=" + networkId
                + " player=" + (runner != null && runner.IsRunning ? runner.LocalPlayer.ToString() : "None")
                + " stateAuthority=" + networkObject.StateAuthority
                + " spawnedBy=" + sharedObject.DebugSpawnedByPlayer
                + " spawnedAt=" + sharedObject.DebugSpawnedAtRunnerTime);
        }
    }

    private void DespawnNetworkBottle(NetworkRunner runner, NetworkObject target)
    {
        string networkId = FormatNetworkId(target);
        runner.Despawn(target);
        observedBottleIds.Remove(networkId);
        localBottleLogIds.Remove(networkId);
        remoteBottleLogIds.Remove(networkId);
        Debug.Log("[PhotonSharedBottleSpawner] BOTTLE_DESPAWN"
            + " networkId=" + networkId
            + " player=" + runner.LocalPlayer);
    }

    private static string FormatNetworkId(NetworkObject networkObject)
    {
        return networkObject != null && networkObject.Id.IsValid
            ? networkObject.Id.ToString()
            : "Invalid";
    }
#endif

    private void FailSpawn(string reason)
    {
        lastSpawnError = reason;
        Debug.LogWarning("[PhotonSharedBottleSpawner] BOTTLE_SPAWN_FAILED reason=" + reason);
    }

    private static string FormatVector(Vector3 value)
    {
        return value.ToString("F3");
    }

    private static string FormatQuaternion(Quaternion value)
    {
        return value.ToString("F3");
    }
}
