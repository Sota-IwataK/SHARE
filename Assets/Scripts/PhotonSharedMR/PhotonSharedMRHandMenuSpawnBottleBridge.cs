using MixedReality.Toolkit;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PhotonSharedMRHandMenuSpawnBottleBridge : MonoBehaviour
{
    private const string HandMenuSpawnSource = "HandMenuSpawnBottle";

    public GameObject handMenuSpawnBottleObject;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public PhotonSharedBottleSpawner sharedBottleSpawner;
    public DetectedBottlePoseSubscriber localBottleSpawner;
    public PhotonDetectedBottleBridge detectedBottleBridge;

    private int lastSpawnFrame = -1;

    public bool HasLocalFallback => localBottleSpawner != null;
    public bool HasPhotonSharedBranch => sharedBottleSpawner != null;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        WireButton();
    }

    private void OnDisable()
    {
        UnwireButton();
    }

    public void SpawnBottleFromHandMenu()
    {
        Debug.Log("[PhotonSharedMRHandMenuSpawnBottleBridge] Spawn Bottle button pressed"
            + " frame=" + Time.frameCount);

        if (lastSpawnFrame == Time.frameCount)
        {
            return;
        }

        lastSpawnFrame = Time.frameCount;
        ResolveReferences();

        bool sharedModeSelected = bootstrap != null && bootstrap.SharedModeSelected;
        LogBottleDiagnostic(sharedModeSelected);
        if (!sharedModeSelected)
        {
            Debug.Log("[PhotonSharedMRHandMenuSpawnBottleBridge] BOTTLE_SPAWN_MODE local"
                + " source=" + HandMenuSpawnSource
                + " reason=SoloModeSelected");
            if (localBottleSpawner == null)
            {
                Debug.LogWarning("[PhotonSharedMRHandMenuSpawnBottleBridge] Local Spawn Bottle fallback is missing.");
                return;
            }

            localBottleSpawner.GenerateOrRefreshBottle();
            return;
        }

        string sharedReason = "PhotonNotJoined";
        if (sharedBottleSpawner != null && sharedBottleSpawner.CanSpawnSharedBottle(out sharedReason))
        {
            Debug.Log("[PhotonSharedMRHandMenuSpawnBottleBridge] BOTTLE_SPAWN_MODE shared"
                + " source=" + HandMenuSpawnSource);
            sharedBottleSpawner.SyncBottlesFromLatestDetections();
            return;
        }

        string detail = string.IsNullOrWhiteSpace(sharedReason) ? "Unknown" : sharedReason;
        Debug.LogWarning("[PhotonSharedMRHandMenuSpawnBottleBridge] BOTTLE_SPAWN_MODE shared rejected"
            + " source=" + HandMenuSpawnSource
            + " reason=" + detail);
    }

    private void ResolveReferences()
    {
        if (sharedBottleSpawner == null)
        {
            sharedBottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        }

        if (bootstrap == null && sharedBottleSpawner != null)
        {
            bootstrap = sharedBottleSpawner.bootstrap;
        }

        EnsureBootstrap(nameof(ResolveReferences), false);

        if (localBottleSpawner == null)
        {
            localBottleSpawner = FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        }

        if (detectedBottleBridge == null)
        {
            detectedBottleBridge = FindObjectOfType<PhotonDetectedBottleBridge>(true);
        }

        if (handMenuSpawnBottleObject == null)
        {
            Transform spawnBottle = FindHandMenuSpawnBottle();
            handMenuSpawnBottleObject = spawnBottle != null ? spawnBottle.gameObject : null;
        }
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private void LogBottleDiagnostic(bool sharedModeSelected)
    {
        bool bootstrapExists = bootstrap != null;
        bool runnerExists = bootstrapExists && bootstrap.RunnerExists;
        bool runnerIsRunning = bootstrapExists && bootstrap.RunnerIsRunning;
        bool spawnerEnabled = sharedBottleSpawner != null && sharedBottleSpawner.enableSharedBottleSpawn;
        bool subscriberExists = sharedBottleSpawner != null
            ? sharedBottleSpawner.detectedBottleSubscriber != null
            : localBottleSpawner != null;
        bool networkPrefabExists = sharedBottleSpawner != null
            && sharedBottleSpawner.networkBottlePrefab != null;
        bool localAvatarExists = NetworkUserAvatar.Local != null;
        Debug.Log("[PhotonSharedBottleDiagnostic]"
            + " sharedSelected=" + sharedModeSelected
            + " bootstrap=" + bootstrapExists
            + " runner=" + runnerExists
            + " running=" + runnerIsRunning
            + " joinStatus=" + (bootstrapExists ? bootstrap.LastJoinStatus : "MissingBootstrap")
            + " enabled=" + spawnerEnabled
            + " subscriber=" + subscriberExists
            + " prefab=" + networkPrefabExists
            + " localAvatar=" + localAvatarExists
            + " roleRestriction=False");
    }

    private void WireButton()
    {
        if (handMenuSpawnBottleObject == null)
        {
            return;
        }

        Button[] buttons = handMenuSpawnBottleObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick.RemoveListener(SpawnBottleFromHandMenu);
            buttons[i].onClick.AddListener(SpawnBottleFromHandMenu);
        }

        StatefulInteractable[] interactables = handMenuSpawnBottleObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].OnClicked.RemoveListener(SpawnBottleFromHandMenu);
            if (!HasPersistentSpawnListener(interactables[i].OnClicked))
            {
                interactables[i].OnClicked.AddListener(SpawnBottleFromHandMenu);
            }
        }
    }

    private bool HasPersistentSpawnListener(UnityEvent unityEvent)
    {
        if (unityEvent == null)
        {
            return false;
        }

        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) == this
                && unityEvent.GetPersistentMethodName(i) == nameof(SpawnBottleFromHandMenu))
            {
                return true;
            }
        }

        return false;
    }

    private void UnwireButton()
    {
        if (handMenuSpawnBottleObject == null)
        {
            return;
        }

        Button[] buttons = handMenuSpawnBottleObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick.RemoveListener(SpawnBottleFromHandMenu);
        }

        StatefulInteractable[] interactables = handMenuSpawnBottleObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].OnClicked.RemoveListener(SpawnBottleFromHandMenu);
        }
    }

    private static Transform FindHandMenuSpawnBottle()
    {
        GameObject handMenu = GameObject.Find("HandMenu");
        if (handMenu == null)
        {
            return null;
        }

        Transform menuContent = FindChildRecursive(handMenu.transform, "MenuContent");
        if (menuContent == null)
        {
            return null;
        }

        Transform directChild = menuContent.Find("Spawn Bottle");
        return directChild != null ? directChild : FindChildRecursive(menuContent, "Spawn Bottle");
    }

    private static Transform FindChildRecursive(Transform root, string objectName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == objectName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindChildRecursive(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
