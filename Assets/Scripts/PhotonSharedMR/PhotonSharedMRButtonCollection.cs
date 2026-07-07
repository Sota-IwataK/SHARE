using System.Collections.Generic;
using MixedReality.Toolkit;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PhotonSharedMRButtonCollection : MonoBehaviour
{
    [Header("Scene References")]
    public PhotonSharedMRLoginPanel loginPanel;
    public PhotonSharedMRLoginPanelVisibilityController loginPanelVisibilityController;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public PhotonSharedBottleSpawner bottleSpawner;
    public PhotonSharedMRDebugPanel debugPanel;

    [Header("Collection References")]
    public GameObject collectionRoot;
    public GameObject connectionButtonObject;
    public Button connectionButton;
    public GameObject retryButtonObject;
    public GameObject spawnButtonObject;
    public GameObject despawnButtonObject;
    public GameObject debugToggleButtonObject;
    public GameObject leaveRoomButtonObject;
    public TMP_Text statusText;
    public TMP_Text errorText;
    public Button retryButton;
    public Button spawnButton;
    public Button despawnButton;
    public Button debugToggleButton;
    public Button leaveRoomButton;

    [Header("Runtime")]
    public float refreshIntervalSeconds = 0.2f;

    private readonly Dictionary<string, int> lastClickFrameByAction = new Dictionary<string, int>();
    private float nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
        ResolveUiReferences();
        WireButtons();
        RefreshStatus(true);
    }

    private void OnEnable()
    {
        ResolveReferences();
        ResolveUiReferences();
        WireButtons();
        RefreshStatus(true);
    }

    private void OnDisable()
    {
        UnwireButtons();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
        {
            return;
        }

        RefreshStatus(false);
        nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshIntervalSeconds);
    }

    public void OpenLogin()
    {
        if (!ConsumeClick("OpenLogin"))
        {
            return;
        }

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

    public void RetryLogin()
    {
        if (!ConsumeClick("RetryLogin"))
        {
            return;
        }

        ResolveReferences();
        if (loginPanel == null)
        {
            SetError("Photon login panel missing.");
            return;
        }

        loginPanel.RetrySessionFromUi();
        RefreshStatus(true);
    }

    public void SpawnSharedBottle()
    {
        if (!ConsumeClick("SpawnSharedBottle"))
        {
            return;
        }

        ResolveReferences();
        if (bottleSpawner != null)
        {
            bottleSpawner.SpawnSharedBottle("PhotonButtonCollection");
        }
    }

    public void DespawnLastBottle()
    {
        if (!ConsumeClick("DespawnLastBottle"))
        {
            return;
        }

        ResolveReferences();
        if (bottleSpawner != null)
        {
            bottleSpawner.RequestDespawnLastSharedBottle();
        }
    }

    public void ToggleDebugPanel()
    {
        if (!ConsumeClick("ToggleDebugPanel"))
        {
            return;
        }

        ResolveReferences();
        if (debugPanel == null)
        {
            SetError("Debug panel missing.");
            return;
        }

        debugPanel.enableDebugPanel = !debugPanel.enableDebugPanel;
        debugPanel.gameObject.SetActive(true);
        RefreshStatus(true);
    }

    public void LeaveRoom()
    {
        if (!ConsumeClick("LeaveRoom"))
        {
            return;
        }

        ResolveReferences();
        if (EnsureBootstrap(nameof(LeaveRoom), true) == null)
        {
            SetError("Photon bootstrap missing.");
            return;
        }

        bootstrap.LeaveRoom();
        if (loginPanel != null)
        {
            loginPanel.ResetStartRequest();
            loginPanel.OpenLoginPanel();
        }

        RefreshStatus(true);
    }

    public void RefreshStatus(bool force)
    {
        ResolveReferences();
        ResolveUiReferences();

        bool joined = bootstrap != null && bootstrap.IsRunning;
        string joinState = bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap";
        string room = bootstrap != null ? bootstrap.DebugRoomName : "MissingBootstrap";
        int players = bootstrap != null ? bootstrap.ActivePlayersCount : 0;
        SharedUserRole role = bootstrap != null ? bootstrap.DebugCurrentRole : SharedUserRole.ManipulatorOperator;
        string error = bootstrap != null ? bootstrap.LastError : "Photon bootstrap missing.";
        bool hasError = !joined && !string.IsNullOrWhiteSpace(error);

        if (statusText != null)
        {
            if (joined)
            {
                statusText.text = "Photon Connection"
                    + "\nStatus: Joined"
                    + "\nRoom: " + room
                    + "\nPlayers: " + players
                    + "\nRole: " + role;
            }
            else
            {
                statusText.text = "Photon Connection"
                    + "\nStatus: " + ResolveDisconnectedState(joinState);
            }
        }

        SetButtonVisible(connectionButtonObject, !joined);
        SetButtonVisible(spawnButtonObject, joined);
        SetButtonVisible(despawnButtonObject, joined);
        SetButtonVisible(debugToggleButtonObject, joined);
        SetButtonVisible(leaveRoomButtonObject, joined);

        SetButtonEnabled(connectionButtonObject, connectionButton, !joined && loginPanel != null);
        SetButtonEnabled(spawnButtonObject, spawnButton, joined && bottleSpawner != null);
        SetButtonEnabled(despawnButtonObject, despawnButton, joined && bottleSpawner != null && bottleSpawner.SharedNetworkBottleCount > 0);
        SetButtonEnabled(debugToggleButtonObject, debugToggleButton, joined && debugPanel != null);
        SetButtonEnabled(leaveRoomButtonObject, leaveRoomButton, joined && bootstrap != null);
        SetButtonVisible(retryButtonObject, hasError);
        SetButtonEnabled(retryButtonObject, retryButton, hasError && loginPanel != null);

        if (debugToggleButtonObject != null)
        {
            SetButtonLabel(debugToggleButtonObject, debugPanel != null && debugPanel.enableDebugPanel
                ? "Debug Panel: ON"
                : "Debug Panel: OFF");
        }

        if (force || hasError)
        {
            SetError(error);
        }
        else if (errorText != null)
        {
            errorText.text = string.Empty;
        }
    }

    private void ResolveReferences()
    {
        if (collectionRoot == null)
        {
            collectionRoot = gameObject;
        }

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

        if (bottleSpawner == null)
        {
            bottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        }

        if (debugPanel == null)
        {
            debugPanel = FindObjectOfType<PhotonSharedMRDebugPanel>(true);
        }
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private void ResolveUiReferences()
    {
        if (collectionRoot == null)
        {
            return;
        }

        connectionButtonObject = connectionButtonObject != null ? connectionButtonObject : FindChildObject("PhotonConnectionButton");
        spawnButtonObject = spawnButtonObject != null ? spawnButtonObject : FindChildObject("SpawnSharedBottleButton");
        despawnButtonObject = despawnButtonObject != null ? despawnButtonObject : FindChildObject("DespawnLastBottleButton");
        debugToggleButtonObject = debugToggleButtonObject != null ? debugToggleButtonObject : FindChildObject("DebugPanelToggleButton");
        leaveRoomButtonObject = leaveRoomButtonObject != null ? leaveRoomButtonObject : FindChildObject("LeaveRoomButton");
        retryButtonObject = retryButtonObject != null ? retryButtonObject : FindChildObject("RetryButton");
        connectionButton = connectionButton != null ? connectionButton : FindButton(connectionButtonObject);
        spawnButton = spawnButton != null ? spawnButton : FindButton(spawnButtonObject);
        despawnButton = despawnButton != null ? despawnButton : FindButton(despawnButtonObject);
        debugToggleButton = debugToggleButton != null ? debugToggleButton : FindButton(debugToggleButtonObject);
        leaveRoomButton = leaveRoomButton != null ? leaveRoomButton : FindButton(leaveRoomButtonObject);
        retryButton = retryButton != null ? retryButton : FindButton(retryButtonObject);
        statusText = statusText != null ? statusText : FindChildText("PhotonStatusText");
        errorText = errorText != null ? errorText : FindChildText("PhotonErrorText");
    }

    private void WireButtons()
    {
        WireButton(connectionButtonObject, connectionButton, OpenLogin);
        WireButton(retryButtonObject, retryButton, RetryLogin);
        WireButton(spawnButtonObject, spawnButton, SpawnSharedBottle);
        WireButton(despawnButtonObject, despawnButton, DespawnLastBottle);
        WireButton(debugToggleButtonObject, debugToggleButton, ToggleDebugPanel);
        WireButton(leaveRoomButtonObject, leaveRoomButton, LeaveRoom);
    }

    private void UnwireButtons()
    {
        UnwireButton(connectionButtonObject, connectionButton, OpenLogin);
        UnwireButton(retryButtonObject, retryButton, RetryLogin);
        UnwireButton(spawnButtonObject, spawnButton, SpawnSharedBottle);
        UnwireButton(despawnButtonObject, despawnButton, DespawnLastBottle);
        UnwireButton(debugToggleButtonObject, debugToggleButton, ToggleDebugPanel);
        UnwireButton(leaveRoomButtonObject, leaveRoomButton, LeaveRoom);
    }

    private bool ConsumeClick(string actionName)
    {
        int frame = Time.frameCount;
        if (lastClickFrameByAction.TryGetValue(actionName, out int lastFrame) && lastFrame == frame)
        {
            return false;
        }

        lastClickFrameByAction[actionName] = frame;
        return true;
    }

    private GameObject FindChildObject(string objectName)
    {
        return FindChild(collectionRoot != null ? collectionRoot.transform : null, objectName);
    }

    private Button FindButton(GameObject buttonObject)
    {
        return buttonObject != null ? buttonObject.GetComponentInChildren<Button>(true) : null;
    }

    private TMP_Text FindChildText(string objectName)
    {
        GameObject child = FindChild(collectionRoot != null ? collectionRoot.transform : null, objectName);
        return child != null ? child.GetComponent<TMP_Text>() : null;
    }

    private void SetError(string message)
    {
        if (errorText != null)
        {
            errorText.text = string.IsNullOrWhiteSpace(message) ? string.Empty : "Last Error: " + message;
        }
    }

    private static string ResolveDisconnectedState(string joinState)
    {
        if (string.IsNullOrWhiteSpace(joinState) || joinState == "NotStarted")
        {
            return "Not Connected";
        }

        if (joinState.IndexOf("Fail", System.StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Error", System.StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Missing", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Failed";
        }

        if (joinState.IndexOf("Start", System.StringComparison.OrdinalIgnoreCase) >= 0
            || joinState.IndexOf("Join", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "Connecting";
        }

        return "Not Connected";
    }

    private static void WireButton(GameObject buttonObject, Button button, UnityEngine.Events.UnityAction action)
    {
        if (buttonObject == null && button == null)
        {
            return;
        }

        if (button != null)
        {
            button.onClick.RemoveListener(action);
            if (!HasPersistentListener(button.onClick, action))
            {
                button.onClick.AddListener(action);
            }
        }

        GameObject root = buttonObject != null ? buttonObject : button.gameObject;
        StatefulInteractable[] interactables = root.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].OnClicked.RemoveListener(action);
            if (!HasPersistentListener(interactables[i].OnClicked, action))
            {
                interactables[i].OnClicked.AddListener(action);
            }
        }
    }

    private static bool HasPersistentListener(UnityEngine.Events.UnityEventBase unityEvent, UnityEngine.Events.UnityAction action)
    {
        if (unityEvent == null || action == null || action.Target == null)
        {
            return false;
        }

        string methodName = action.Method != null ? action.Method.Name : string.Empty;
        Object target = action.Target as Object;
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

    private static void UnwireButton(GameObject buttonObject, Button button, UnityEngine.Events.UnityAction action)
    {
        if (buttonObject == null && button == null)
        {
            return;
        }

        if (button != null)
        {
            button.onClick.RemoveListener(action);
        }

        GameObject root = buttonObject != null ? buttonObject : button.gameObject;
        StatefulInteractable[] interactables = root.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].OnClicked.RemoveListener(action);
        }
    }

    private static void SetButtonVisible(GameObject buttonObject, bool visible)
    {
        if (buttonObject != null)
        {
            buttonObject.SetActive(visible);
        }
    }

    private static void SetButtonEnabled(GameObject buttonObject, Button button, bool enabled)
    {
        GameObject root = buttonObject != null ? buttonObject : (button != null ? button.gameObject : null);
        if (root == null)
        {
            return;
        }

        if (button != null)
        {
            button.interactable = enabled;
        }

        StatefulInteractable[] interactables = root.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].enabled = enabled;
        }

        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = enabled;
        }
    }

    private static void SetButtonLabel(GameObject buttonObject, string label)
    {
        if (buttonObject == null)
        {
            return;
        }

        TMP_Text[] labels = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i] == null || IsIconText(labels[i].transform))
            {
                continue;
            }

            labels[i].text = label;
        }
    }

    private static bool IsIconText(Transform transform)
    {
        while (transform != null)
        {
            if (transform.name.IndexOf("Icon", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            transform = transform.parent;
        }

        return false;
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
}
