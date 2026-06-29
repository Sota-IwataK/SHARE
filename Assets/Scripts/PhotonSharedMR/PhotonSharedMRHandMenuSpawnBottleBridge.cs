using MixedReality.Toolkit;
using UnityEngine;
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
        if (lastSpawnFrame == Time.frameCount)
        {
            return;
        }

        lastSpawnFrame = Time.frameCount;
        ResolveReferences();

        string sharedReason = "PhotonNotJoined";
        if (sharedBottleSpawner != null && sharedBottleSpawner.CanSpawnSharedBottle(out sharedReason))
        {
            if (detectedBottleBridge != null
                && detectedBottleBridge.TryGetAuthorityDetectedBottlePose(out UnityEngine.Pose detectedPose))
            {
                NetworkedSharedSceneObject existingDetectedBottle = sharedBottleSpawner.FindLatestSharedBottle();
                if (existingDetectedBottle != null && existingDetectedBottle.IsPhotonSharedNetworkBottle)
                {
                    Debug.Log("[PhotonSharedMRHandMenuSpawnBottleBridge] BOTTLE_SPAWN_MODE shared"
                        + " source=" + HandMenuSpawnSource
                        + " reason=DetectedSharedBottleAlreadyExists");
                    return;
                }

                sharedBottleSpawner.SpawnSharedBottleAtPose(
                    detectedPose.position,
                    detectedPose.rotation,
                    HandMenuSpawnSource);
                return;
            }

            sharedBottleSpawner.SpawnSharedBottle(HandMenuSpawnSource);
            return;
        }

        string detail = string.IsNullOrWhiteSpace(sharedReason) ? "Unknown" : sharedReason;
        Debug.Log("[PhotonSharedMRHandMenuSpawnBottleBridge] BOTTLE_SPAWN_MODE local"
            + " source=" + HandMenuSpawnSource
            + " reason=PhotonNotJoined"
            + " detail=" + detail);

        if (localBottleSpawner == null)
        {
            Debug.LogWarning("[PhotonSharedMRHandMenuSpawnBottleBridge] Local Spawn Bottle fallback is missing.");
            return;
        }

        localBottleSpawner.SpawnBottleManual();
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
            interactables[i].OnClicked.AddListener(SpawnBottleFromHandMenu);
        }
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
