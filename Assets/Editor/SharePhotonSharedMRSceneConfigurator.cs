using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MixedReality.Toolkit;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if FUSION_WEAVER && FUSION2
using Fusion;
using Fusion.Editor;
#endif

public static class SharePhotonSharedMRSceneConfigurator
{
    private const string MainScenePath = "Assets/Scenes/main.unity";
    private const string PrefabFolder = "Assets/Prefabs/PhotonSharedMR";
    private const string AvatarPrefabPath = PrefabFolder + "/NetworkUserAvatar.prefab";
    private const string LegacyNetworkBottlePrefabPath = PrefabFolder + "/NetworkBottlePrefab.prefab";
    private const string PrehubBottlePrefabPath = "Assets/Prehabs/bottle1.prefab";
    private const string PhotonSharedBottlePrefabPath = PrefabFolder + "/PhotonSharedBottle_PREHUB_bottle1.prefab";
    private const string NetworkBottleMaterialPath = PrefabFolder + "/PhotonSharedBottleUnlit.mat";
    private const string SharedBottleLayerName = "PhotonSharedBottle";
    private const string PhotonSettingEntryName = "Photon setting";
    private const string PhotonSettingVisualName = "PhotonSettingButtonVisual";
    private const string PhotonSettingInteractableName = "PhotonSettingInteractable";
    private const string ButtonCollectionName = "ButtonCollection";
    private const string PhotonButtonCollectionName = "PhotonButtonCollection";
    private const string PhotonConnectionButtonName = "PhotonConnectionButton";
    private const string PhotonStatusPanelName = "PhotonStatusPanel";
    private const string PhotonStatusTextName = "PhotonStatusText";
    private const string PhotonErrorTextName = "PhotonErrorText";
    private const string SpawnSharedBottleButtonName = "SpawnSharedBottleButton";
    private const string DespawnLastBottleButtonName = "DespawnLastBottleButton";
    private const string DebugPanelToggleButtonName = "DebugPanelToggleButton";
    private const string LeaveRoomButtonName = "LeaveRoomButton";
    private const string RetryButtonName = "RetryButton";
    private const string MrtkPressableButtonIconTextGuid = "e5b53b02ddc453d49a3a36a85164095f";
    private const string HandMenuSpawnBottleName = "Spawn Bottle";
    private const string HandMenuSpawnBottleBridgeName = "HandMenuSpawnBottleBridge";
    private const string PhotonDetectedBottleBridgeName = "PhotonDetectedBottleBridge";
    private const string RobotMoveSettingName = "Robot Move setting";
    private const string RobotMoveActionName = "Robot Move Action";
    private static readonly string[] PhotonJoinProtectedHandMenuItems =
    {
        "CalibrationSelect",
        "CalibrationOperation",
        "Origin Set",
        RobotMoveSettingName,
        RobotMoveActionName
    };

    [MenuItem("SHARE/Photon Shared MR/Configure Main Scene")]
    public static void ConfigureMainSceneMenu()
    {
        ConfigureMainScene();
    }

    [MenuItem("SHARE/Photon Shared MR/Verify Main Scene")]
    public static void VerifyMainSceneMenu()
    {
        VerifyMainScene();
    }

    public static void ConfigureAvatarPrefabOnly()
    {
        Directory.CreateDirectory(PrefabFolder);
        EnsureAvatarPrefab();
        AssetDatabase.SaveAssets();

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(0);
        }
    }

    public static void ConfigureMainSceneThreeTimesAndVerify()
    {
        for (int i = 0; i < 3; i++)
        {
            ConfigureMainScene();
        }

        VerifyMainScene();

        if (Application.isBatchMode)
        {
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
    }

    public static void ConfigureMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(PrefabFolder);

        GameObject avatarPrefab = EnsureAvatarPrefab();
        GameObject networkBottlePrefab = EnsurePhotonSharedBottlePrefab();

        GameObject root = EnsureSceneObject("PhotonSharedMR");
        root.transform.SetParent(null, true);
        root.SetActive(true);
        EnsureComponent<PhotonSharedMRRootLifetimeLogger>(root);
        DestroyChildIfExists(root, "LoginPanel");
        DestroyChildIfExists(root, "NetworkedVirtualAmir");
        DestroyChildIfExists(root, "PhotonSharedMRGestureMenuController");
        GameObject bootstrapObject = EnsureSceneObject("PhotonSharedRoomBootstrap");
        bootstrapObject.transform.SetParent(null, true);
        bootstrapObject.SetActive(true);
        PhotonFusionSharedRoomBootstrap bootstrap = EnsureComponent<PhotonFusionSharedRoomBootstrap>(bootstrapObject);
        bootstrap.roomName = "SHARE-MR-Room";
        bootstrap.autoJoinOnStart = false;
        bootstrap.maxPlayers = 8;
        bootstrap.initialRole = SharedUserRole.ManipulatorOperator;
        bootstrap.defaultSessionSettings = PhotonSharedMRSessionSettings.CreateDefault();
        bootstrap.defaultSessionSettings.roomName = PhotonSharedMRSessionSettings.DefaultRoomName;
        bootstrap.useAutoHostOrClient = true;
        bootstrap.networkUserAvatarPrefab = avatarPrefab;
        bootstrap.headSource = Camera.main != null ? Camera.main.transform : null;
        bootstrap.leftHandSource = FindTransform("LeftHand") ?? FindTransform("XRHand_Palm");
        bootstrap.rightHandSource = FindTransform("RightHand");
#if FUSION_WEAVER && FUSION2
        EnsureComponent<NetworkRunner>(bootstrapObject);
#endif

        GameObject sharedObjectsRoot = EnsureChild(root, "NetworkedSharedObjects");
        GameObject bottle = EnsurePrimitiveChild(sharedObjectsRoot, "NetworkedBottleProxy", PrimitiveType.Cylinder, new Vector3(0.12f, 0.25f, 0.12f), new Vector3(0.35f, 0.75f, 0.8f), new Color(0.15f, 0.55f, 1f, 1f));
        ConfigureSharedObject(bottle, SharedNetworkObjectKind.Bottle);

        GameObject box = EnsurePrimitiveChild(sharedObjectsRoot, "NetworkedBoxProxy", PrimitiveType.Cube, new Vector3(0.22f, 0.16f, 0.22f), new Vector3(-0.35f, 0.65f, 0.85f), new Color(0.8f, 0.55f, 0.25f, 1f));
        ConfigureSharedObject(box, SharedNetworkObjectKind.Box);

        GameObject obstacle = EnsurePrimitiveChild(sharedObjectsRoot, "NetworkedObstacleProxy", PrimitiveType.Cube, new Vector3(0.18f, 0.5f, 0.18f), new Vector3(0f, 0.85f, 1.15f), new Color(0.8f, 0.12f, 0.12f, 1f));
        ConfigureSharedObject(obstacle, SharedNetworkObjectKind.Obstacle);

        GameObject manipulatorRoot = EnsureChild(root, "ManipulatorOperatorInfo");
        GameObject endEffectorInfo = EnsureTextChild(manipulatorRoot, "EndEffectorInfo", "End effector / grasp area", new Vector3(0.55f, 1.15f, 0.65f), Color.cyan);
        GameObject graspArea = EnsurePrimitiveChild(manipulatorRoot, "GraspAreaProxy", PrimitiveType.Sphere, Vector3.one * 0.18f, new Vector3(0.35f, 0.75f, 0.8f), new Color(0.1f, 1f, 0.4f, 0.35f));
        GameObject predictedTrajectory = EnsureLineChild(manipulatorRoot, "PredictedArmTrajectory", new[]
        {
            new Vector3(0f, 0.85f, 1.05f),
            new Vector3(0.18f, 1.0f, 0.95f),
            new Vector3(0.35f, 0.75f, 0.8f)
        }, new Color(1f, 0.8f, 0.1f, 1f));

        GameObject scoutRoot = EnsureChild(root, "ScoutInfo");
        GameObject layout = EnsureLineChild(scoutRoot, "SharedLayoutOverview", new[]
        {
            new Vector3(-0.7f, 0.48f, 0.55f),
            new Vector3(0.7f, 0.48f, 0.55f),
            new Vector3(0.7f, 0.48f, 1.45f),
            new Vector3(-0.7f, 0.48f, 1.45f),
            new Vector3(-0.7f, 0.48f, 0.55f)
        }, new Color(0.2f, 0.9f, 0.95f, 1f));
        GameObject candidates = EnsureTextChild(scoutRoot, "ObjectCandidateInfo", "Object candidates / user positions", new Vector3(-0.55f, 1.15f, 0.75f), Color.green);

        GameObject supervisorRoot = EnsureChild(root, "SupervisorInfo");
        GameObject allUsers = EnsureTextChild(supervisorRoot, "AllUserStateInfo", "Users: role / state / lock owner", new Vector3(-0.2f, 1.2f, 0.62f), Color.white);
        GameObject taskProgress = EnsureTextChild(supervisorRoot, "TaskProgressInfo", "Task progress: virtual only", new Vector3(-0.2f, 1.08f, 0.62f), Color.yellow);
        GameObject risk = EnsureTextChild(supervisorRoot, "RiskInfo", "Risk display: simulated", new Vector3(-0.2f, 0.96f, 0.62f), Color.red);

        GameObject filterObject = EnsureChild(root, "RoleBasedInfoFilter");
        RoleBasedInfoFilter filter = EnsureComponent<RoleBasedInfoFilter>(filterObject);
        filter.useManualRoleOverride = true;
        filter.manualRole = SharedUserRole.ManipulatorOperator;
        filter.alwaysVisibleObjects = new[] { sharedObjectsRoot };
        filter.manipulatorBottleObjects = new[] { bottle };
        filter.manipulatorEndEffectorObjects = new[] { endEffectorInfo };
        filter.manipulatorGraspAreaObjects = new[] { graspArea };
        filter.manipulatorPredictedTrajectoryObjects = new[] { predictedTrajectory };
        filter.scoutLayoutObjects = new[] { layout };
        filter.scoutObjectCandidateObjects = new[] { candidates, bottle, box, obstacle };
        filter.scoutOtherUserObjects = new[] { candidates };
        filter.supervisorAllUserStateObjects = new[] { allUsers };
        filter.supervisorTaskProgressObjects = new[] { taskProgress };
        filter.supervisorRiskObjects = new[] { risk };
        filter.manipulatorShowsHmdOverheadCursors = true;
        filter.scoutShowsHmdOverheadCursors = true;
        filter.supervisorShowsHmdOverheadCursors = true;

        GameObject loginObject = EnsureChild(root, "PhotonSharedMRLoginController");
        DestroyChildIfExists(loginObject, "LoginPanelCanvas");
        DestroyChildIfExists(loginObject, "StartupPhotonLoginPanelCanvas");
        PhotonSharedMRLoginPanel loginPanel = EnsureComponent<PhotonSharedMRLoginPanel>(loginObject);
        loginPanel.bootstrap = bootstrap;
        loginPanel.roleFilter = filter;
        loginPanel.defaultSettings = PhotonSharedMRSessionSettings.CreateDefault();
        loginPanel.defaultSettings.roomName = PhotonSharedMRSessionSettings.DefaultRoomName;
        loginPanel.suppressBootstrapAutoJoin = true;
        loginPanel.hidePanelAfterStart = true;
        loginPanel.enableRuntimePanel = true;
        loginPanel.showOnStart = true;
        loginPanel.reopenOnLeaveOrDisconnect = true;
        loginPanel.hideRobotSelectionForPcObserver = true;
        loginPanel.spawnDistance = 0.90f;
        loginPanel.verticalOffset = -0.12f;
        loginPanel.panelScale = 0.001f;
        loginPanel.loginDistanceFromHead = 0.90f;
        loginPanel.loginHorizontalOffset = 0.00f;
        loginPanel.loginVerticalOffset = -0.12f;
        loginPanel.loginScale = 0.001f;
        loginPanel.faceCameraOnOpen = true;
        loginPanel.EnsurePanel();
        EnsureStartupLoginPanelPersistentWiring(loginPanel);
        loginPanel.SetPanelVisible(false);

        GameObject spawnerObject = EnsureChild(root, "PhotonSharedBottleSpawner");
        DestroyChildIfExists(spawnerObject, "SharedBottleSpawnerCanvas");
        PhotonSharedBottleSpawner bottleSpawner = EnsureComponent<PhotonSharedBottleSpawner>(spawnerObject);
        bottleSpawner.bootstrap = bootstrap;
        bottleSpawner.networkBottlePrefab = networkBottlePrefab;
        bottleSpawner.spawnAnchor = Camera.main != null ? Camera.main.transform : null;
        bottleSpawner.enableSharedBottleSpawn = true;
        bottleSpawner.enableSpawnControls = false;
        bottleSpawner.spawnDistance = 0.65f;
        bottleSpawner.spawnVerticalOffset = -0.08f;
        bottleSpawner.maxSharedBottleCount = 8;
        bottleSpawner.controlsDistance = 0.95f;
        bottleSpawner.controlsVerticalOffset = -0.42f;
        bottleSpawner.controlsScale = 0.0012f;

        PhotonDetectedBottleBridge detectedBottleBridge = EnsurePhotonDetectedBottleBridge(root, bootstrap, bottleSpawner);
        PhotonSharedMRHandMenuSpawnBottleBridge handMenuSpawnBridge = EnsureHandMenuSpawnBottleBridge(root, bootstrap, bottleSpawner, detectedBottleBridge);

        GameObject debugPanelObject = EnsureChild(root, "DebugPanel");
        PhotonSharedMRDebugPanel debugPanel = EnsureComponent<PhotonSharedMRDebugPanel>(debugPanelObject);
        debugPanel.enableDebugPanel = false;
        debugPanel.bootstrap = bootstrap;
        debugPanel.roleFilter = filter;
        debugPanel.bottleSpawner = bottleSpawner;
        debugPanel.detectedBottleBridge = detectedBottleBridge;
        debugPanel.followTarget = Camera.main != null ? Camera.main.transform : null;
        debugPanel.panelDistance = 1.15f;
        debugPanel.panelVerticalOffset = -0.18f;
        debugPanel.panelScale = 0.025f;
        debugPanel.refreshIntervalSeconds = 0.2f;
        debugPanel.fontSize = 2.0f;

        GameObject menuPanelObject = EnsureChild(root, "PhotonSharedMRMenuPanel");
        PhotonSharedMRMenuPanel menuPanel = EnsureComponent<PhotonSharedMRMenuPanel>(menuPanelObject);
        DisableLegacyPhotonSettingEntry();
        menuPanel.existingMenuRoot = FindExistingMenuRoot();
        menuPanel.loginPanel = loginPanel;
        menuPanel.bootstrap = bootstrap;
        menuPanel.roleFilter = filter;
        menuPanel.bottleSpawner = bottleSpawner;
        menuPanel.debugPanel = debugPanel;
        menuPanel.enableMenuPanel = false;
        menuPanel.showOnStart = false;
        menuPanel.collapseSettingsAfterJoin = true;
        menuPanel.allowLegacyJoinControls = false;
        menuPanel.panelAnchoredPosition = new Vector2(145f, -12f);
        menuPanel.panelSize = new Vector2(292f, 620f);
        menuPanel.enableMenuDebugLogs = false;
        menuPanel.connectExistingPhotonSettingButton = false;
        menuPanel.SetMenuVisible(false);
        menuPanel.enabled = false;

        PhotonSharedMRButtonCollection photonButtons = EnsurePhotonButtonCollection(
            loginPanel,
            bootstrap,
            bottleSpawner,
            debugPanel);
        if (photonButtons != null)
        {
            EditorUtility.SetDirty(photonButtons);
        }

        EnsureRobotMoveButtonWiring();
        RemovePhotonJoinCallbacksFromProtectedHandMenuItems();

        EditorUtility.SetDirty(root);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();

#if !FUSION_WEAVER || !FUSION2
        Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Photon Fusion 2 defines were not active. "
            + "The scene was configured with SHARE placeholder components only. "
            + "Import Photon Fusion 2, confirm FUSION_WEAVER and FUSION2 are defined, then run this menu again to add Fusion NetworkObject components.");
#endif
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] Configured " + MainScenePath);
    }

    public static void VerifyMainScene()
    {
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY root PhotonSharedMR="
            + (GameObject.Find("PhotonSharedMR") != null));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY bootstrap="
            + HasComponent<PhotonFusionSharedRoomBootstrap>("PhotonSharedRoomBootstrap"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY roleFilter="
            + HasComponent<RoleBasedInfoFilter>("RoleBasedInfoFilter"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY loginController="
            + HasComponent<PhotonSharedMRLoginPanel>("PhotonSharedMRLoginController"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY menuPanel="
            + HasComponent<PhotonSharedMRMenuPanel>("PhotonSharedMRMenuPanel"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY photonSettingEntry="
            + (CountSceneObjectsNamed("Photon setting") + CountSceneObjectsNamed("Photon Setting") > 0));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PATHS"
            + " ButtonCollection=" + GetSceneObjectPath(ButtonCollectionName)
            + " PhotonButtonCollection=" + GetSceneObjectPath(PhotonButtonCollectionName)
            + " PhotonSettingEntry=" + GetSceneObjectPath("Photon setting")
            + " PhotonSharedMRMenuSection=" + GetSceneObjectPath("PhotonSharedMRMenuSection")
            + " PhotonSharedMRMenuPanel=" + GetSceneObjectPath("PhotonSharedMRMenuPanel")
            + " PhotonSharedMRLoginController=" + GetSceneObjectPath("PhotonSharedMRLoginController")
            + " StartupPhotonLoginPanelCanvas=" + GetSceneObjectPath("StartupPhotonLoginPanelCanvas")
            + " StartupPhotonLoginPanel=" + GetSceneObjectPath("StartupPhotonLoginPanel"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY gestureMenu="
            + HasComponent<PhotonSharedMRGestureMenuController>("PhotonSharedMRGestureMenuController"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY debugPanel="
            + HasComponent<PhotonSharedMRDebugPanel>("DebugPanel"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY bottleSpawner="
            + HasComponent<PhotonSharedBottleSpawner>("PhotonSharedBottleSpawner"));

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_COUNTS"
            + " PhotonSharedMR=" + CountSceneObjectsNamed("PhotonSharedMR")
            + " LoginPanel=" + CountSceneObjectsNamed("LoginPanel")
            + " PhotonSharedMRLoginController=" + CountSceneObjectsNamed("PhotonSharedMRLoginController")
            + " PhotonSharedMRMenuPanel=" + CountSceneObjectsNamed("PhotonSharedMRMenuPanel")
            + " PhotonSettingEntry=" + (CountSceneObjectsNamed("Photon setting") + CountSceneObjectsNamed("Photon Setting"))
            + " ButtonCollection=" + CountSceneObjectsNamed(ButtonCollectionName)
            + " PhotonButtonCollection=" + CountSceneObjectsNamed(PhotonButtonCollectionName)
            + " PhotonConnectionButton=" + CountSceneObjectsNamed(PhotonConnectionButtonName)
            + " PhotonStatusPanel=" + CountSceneObjectsNamed(PhotonStatusPanelName)
            + " SpawnSharedBottleButton=" + CountSceneObjectsNamed(SpawnSharedBottleButtonName)
            + " DespawnLastBottleButton=" + CountSceneObjectsNamed(DespawnLastBottleButtonName)
            + " DebugPanelToggleButton=" + CountSceneObjectsNamed(DebugPanelToggleButtonName)
            + " LeaveRoomButton=" + CountSceneObjectsNamed(LeaveRoomButtonName)
            + " HandMenuSpawnBottleBridge=" + CountSceneObjectsNamed(HandMenuSpawnBottleBridgeName)
            + " PhotonDetectedBottleBridge=" + CountSceneObjectsNamed(PhotonDetectedBottleBridgeName)
            + " PhotonSharedMRGestureMenuController=" + CountSceneObjectsNamed("PhotonSharedMRGestureMenuController")
            + " PhotonSharedMRMenuSection=" + CountSceneObjectsNamed("PhotonSharedMRMenuSection")
            + " NetworkedVirtualAmir=" + CountSceneObjectsNamed("NetworkedVirtualAmir")
            + " LoginPanelCanvas=" + CountSceneObjectsNamed("LoginPanelCanvas")
            + " StartupPhotonLoginPanelCanvas=" + CountSceneObjectsNamed("StartupPhotonLoginPanelCanvas")
            + " StartupPhotonLoginPanel=" + CountSceneObjectsNamed("StartupPhotonLoginPanel")
            + " HmdFrontSpawnUI=" + CountHmdFrontSpawnUiObjects()
            + " DebugPanel=" + CountSceneObjectsNamed("DebugPanel")
            + " PhotonSharedBottleSpawner=" + CountSceneObjectsNamed("PhotonSharedBottleSpawner")
            + " PhotonSharedRoomBootstrap=" + CountSceneObjectsNamed("PhotonSharedRoomBootstrap")
            + " HmdOverheadCursorScene=" + CountComponentsInScene<HmdOverheadCursor>()
            + " HmdOverheadCursorPrefab=" + PrefabComponentCount<HmdOverheadCursor>(AvatarPrefabPath));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_BUTTON_COLLECTION"
            + " Path=" + GetSceneObjectPath(PhotonButtonCollectionName)
            + " DirectChildren=" + GetPhotonButtonCollectionDirectChildNames()
            + " TemplatePrefab=" + GetPhotonButtonTemplatePrefabPath()
            + " Manager=" + HasComponentIncludingInactive<PhotonSharedMRButtonCollection>(PhotonButtonCollectionName));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_BUTTON_COMPONENTS"
            + " PhotonConnectionButton=" + DescribeButtonComponents(PhotonConnectionButtonName)
            + " SpawnSharedBottleButton=" + DescribeButtonComponents(SpawnSharedBottleButtonName)
            + " DespawnLastBottleButton=" + DescribeButtonComponents(DespawnLastBottleButtonName)
            + " DebugPanelToggleButton=" + DescribeButtonComponents(DebugPanelToggleButtonName)
            + " LeaveRoomButton=" + DescribeButtonComponents(LeaveRoomButtonName)
            + " RetryButton=" + DescribeButtonComponents(RetryButtonName));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_SETTING_HIERARCHY"
            + " PhotonSettingPath=" + GetSceneObjectPath(PhotonSettingEntryName)
            + " DirectChildren=" + GetPhotonSettingDirectChildNames()
            + " NonPhotonDirectChildren=" + CountPhotonSettingNonPhotonDirectChildren()
            + " ExistingFunctionalDirectChildren=" + CountPhotonSettingExistingFunctionalDirectChildren());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_SETTING_INTERACTABLE"
            + " PhotonSettingActive=" + IsSceneObjectActiveSelf(PhotonSettingEntryName)
            + " PhotonSettingBoxCollider=" + HasEnabledComponentIncludingInactive<BoxCollider>(PhotonSettingEntryName)
            + " PhotonSettingStatefulInteractable=" + HasEnabledComponentIncludingInactive<StatefulInteractable>(PhotonSettingEntryName)
            + " PhotonSettingButton=" + HasEnabledComponentIncludingInactive<Button>(PhotonSettingEntryName)
            + " PhotonSettingInteractableCount=" + CountSceneObjectsNamed(PhotonSettingInteractableName)
            + " PhotonSettingInteractableBoxCollider=" + HasEnabledComponentIncludingInactive<BoxCollider>(PhotonSettingInteractableName)
            + " PhotonSettingInteractableStatefulInteractable=" + HasEnabledComponentIncludingInactive<StatefulInteractable>(PhotonSettingInteractableName)
            + " PhotonSettingInteractableButton=" + HasEnabledComponentIncludingInactive<Button>(PhotonSettingInteractableName));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_LOGIN_ROBOT_SELECTION"
            + " PhotonLoginUserNameInputCount=" + CountLoginInputNamed("UserNameInput")
            + " PhotonLoginRoomNameInputCount=" + CountLoginInputNamed("RoomNameInput")
            + " PhotonLoginKeyboardRequired=" + PhotonLoginKeyboardRequired()
            + " PhotonLoginRobotSelectionCount=" + CountLoginRobotSelectionButtons()
            + " PhotonLoginJoinButtonCount=" + CountLoginButtonNamed("StartButton")
            + " PhotonFixedRoomName=" + PhotonFixedRoomName()
            + " PhotonUsesAutoHostOrClient=" + PhotonUsesAutoHostOrClient()
            + " PhotonPlayerDisplayNumberNetworked=" + PhotonPlayerDisplayNumberNetworked()
            + " PhotonAvatarDisplayNameUsesPlayerNumber=" + PhotonAvatarDisplayNameUsesPlayerNumber()
            + " PCEditorAutoObserver=" + PCEditorAutoObserver()
            + " PCEditorRobotSelectionUiHidden=" + PCEditorRobotSelectionUiHidden()
            + " PCEditorRole=" + PCEditorRole()
            + " PCEditorRobotTarget=" + PCEditorRobotTarget()
            + " ObserverDisplayNumberNetworked=" + ObserverDisplayNumberNetworked()
            + " ObserverDisplayNameFormat=" + ObserverDisplayNameFormat()
            + " ObserverNumberUsesPlayerRef=" + ObserverNumberUsesPlayerRef()
            + " ObserverNumbersNotReusedAfterLeave=" + ObserverNumbersNotReusedAfterLeave()
            + " AmirButtonFound=" + (CountLoginButtonNamed("AmirButton") > 0)
            + " RoverButtonFound=" + (CountLoginButtonNamed("RoverButton") > 0)
            + " DroneButtonFound=" + (CountLoginButtonNamed("DroneButton") > 0)
            + " ObserverButtonFound=" + (CountLoginButtonNamed("ObserverButton") > 0)
            + " RobotSelectionButtonsHavePhotonJoinRefs=" + (CountRobotSelectionButtonPhotonJoinRefs() > 0)
            + " JoinButtonStartSessionRefs=" + CountJoinButtonStartSessionRefs());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_HANDMENU_SPAWN_BOTTLE"
            + " HandMenuSpawnBottle=" + (FindHandMenuSpawnBottleTransform() != null ? 1 : 0)
            + " HandMenuSpawnBottlePath=" + GetHandMenuSpawnBottlePath()
            + " HandMenuSpawnBottleOnClicked=" + CountHandMenuSpawnBottleBridgeCallbacks()
            + " HandMenuSpawnBottleHasLocalFallback=" + HandMenuSpawnBottleHasLocalFallback()
            + " HandMenuSpawnBottleHasPhotonSharedBranch=" + HandMenuSpawnBottleHasPhotonSharedBranch()
            + " PhotonSharedBottlePrefabRegistered=" + IsPhotonSharedBottlePrefabRegistered()
            + " NetworkTransform=" + PhotonSharedBottlePrefabHasNetworkTransform());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_DETECTED_BOTTLE_BRIDGE"
            + " PhotonDetectedBottleBridge=" + CountSceneObjectsNamed(PhotonDetectedBottleBridgeName)
            + " Path=" + GetSceneObjectPath(PhotonDetectedBottleBridgeName)
            + " HasSubscriber=" + PhotonDetectedBottleBridgeHasSubscriber()
            + " HasSpawner=" + PhotonDetectedBottleBridgeHasSpawner()
            + " HasBootstrap=" + PhotonDetectedBottleBridgeHasBootstrap()
            + " AuthorityMode=" + PhotonDetectedBottleBridgeAuthorityMode()
            + " DetectedBottlePoseSubscriber=" + CountComponentsInScene<DetectedBottlePoseSubscriber>()
            + " Topic=" + DetectedBottlePoseSubscriberTopic()
            + " PoseArrayTopic=" + DetectedBottlePoseSubscriberPoseArrayTopic()
            + " NetworkTransform=" + PhotonSharedBottlePrefabHasNetworkTransform());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_CALIBRATION_PHOTON_ISOLATION"
            + " PhotonRootOutsideCalibrationHierarchy=" + PhotonRootOutsideCalibrationHierarchy()
            + " NetworkRunnerOutsideCalibrationHierarchy=" + NetworkRunnerOutsideCalibrationHierarchy()
            + " CalibrationDoesNotReferencePhotonRoot=" + CalibrationDoesNotReferencePhotonRoot()
            + " PhotonSharedBottleVisualController=" + PrefabHasComponent<PhotonSharedBottleVisualController>(PhotonSharedBottlePrefabPath)
            + " PhotonSharedBottleRendererCount=" + PrefabRendererCount(PhotonSharedBottlePrefabPath));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_BOOTSTRAP_STABILITY"
            + " PhotonBootstrapCount=" + CountComponentsInScene<PhotonFusionSharedRoomBootstrap>()
            + " PhotonBootstrapSceneRoot=" + PhotonBootstrapSceneRoot()
            + " PhotonBootstrapOutsideCalibrationHierarchy=" + PhotonBootstrapOutsideCalibrationHierarchy()
            + " PhotonRunnerCount=" + PhotonRunnerCount()
            + " PhotonRunnerOutsideCalibrationHierarchy=" + NetworkRunnerOutsideCalibrationHierarchy()
            + " PhotonScriptsUseEnsureBootstrap=" + PhotonScriptsUseEnsureBootstrap());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_CONNECTION_LIFETIME"
            + " PhotonRunnerCount=" + PhotonRunnerCount()
            + " PhotonBootstrapCount=" + CountComponentsInScene<PhotonFusionSharedRoomBootstrap>()
            + " PhotonRunnerManagedOnlyByBootstrap=" + PhotonRunnerManagedOnlyByBootstrap()
            + " PhotonUnexpectedShutdownCallSites=" + PhotonUnexpectedShutdownCallSites()
            + " PhotonUnexpectedStartSessionCallSites=" + PhotonUnexpectedStartSessionCallSites()
            + " PhotonCalibrationCanShutdownRunner=" + PhotonCalibrationCanShutdownRunner()
            + " PhotonUiCanShutdownRunner=" + PhotonUiCanShutdownRunner()
            + " PhotonJoinedStateDoesNotRejoin=" + PhotonJoinedStateDoesNotRejoin()
            + " PhotonLeaveButtonShutdownRefs=" + PhotonLeaveButtonShutdownRefs());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_CALIBRATION_DIAGNOSTICS"
            + " FusionShutdownDiagnostics=" + SourceContains("Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs", "PHOTON_FUSION_SHUTDOWN")
            + " FusionDisconnectDiagnostics=" + SourceContains("Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs", "PHOTON_FUSION_DISCONNECTED")
            + " CalibrationFrameLogging=" + (SourceContains("Assets/Scripts/PhotonSharedMR/PhotonSharedMRCalibrationGuard.cs", "PHOTON_CALIBRATION_FRAME")
                && SourceContains("Assets/Scripts/PhotonSharedMR/PhotonSharedMRRootLifetimeLogger.cs", "TickCalibrationFrame"))
            + " CalibrationStallLogging=" + SourceContains("Assets/Scripts/PhotonSharedMR/PhotonSharedMRCalibrationGuard.cs", "PHOTON_CALIBRATION_FRAME_STALL")
            + " CalibrationExceptionLogging=" + (SourceContains("Assets/Scripts/Calibration/SelectObject.cs", "LogCalibrationException")
                && SourceContains("Assets/Scripts/Calibration/ObjectCalobration.cs", "LogCalibrationException"))
            + " CalibrationBlockingCallSites=" + CountCalibrationBlockingCallSites());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_MOVED_HANDMENU_ITEMS"
            + " CalibrationSelect=" + CountDirectMenuContentChildrenNamed("CalibrationSelect")
            + " CalibrationOperation=" + CountDirectMenuContentChildrenNamed("CalibrationOperation")
            + " RobotMoveSetting=" + CountDirectMenuContentChildrenNamed("Robot Move setting")
            + " RobotMoveAction=" + CountDirectMenuContentChildrenNamed("Robot Move Action")
            + " GripperSet=" + CountDirectMenuContentChildrenNamed("Gripper Set")
            + " OriginSet=" + CountDirectMenuContentChildrenStartingWith("Origin Set")
            + " Stop=" + CountDirectMenuContentChildrenNamed("Stop")
            + " AddView=" + CountDirectMenuContentChildrenNamed("AddView")
            + " IRM=" + CountDirectMenuContentChildrenNamed("IRM")
            + " guras=" + CountDirectMenuContentChildrenNamed("guras")
            + " SpawnBottle=" + CountDirectMenuContentChildrenNamed("Spawn Bottle"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_MOVED_HANDMENU_ITEM_PATHS "
            + GetMovedHandMenuItemPaths());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_ROBOT_MOVE_ACTION"
            + " RobotMoveActionButton=" + CountHandMenuMenuItemsNamed(RobotMoveActionName)
            + " RobotMoveActionPath=" + GetHandMenuMenuItemPath(RobotMoveActionName)
            + " RobotMoveActionOnClicked=" + CountRobotMoveActionOnClicked()
            + " RobotMoveSettingButton=" + CountHandMenuMenuItemsNamed(RobotMoveSettingName)
            + " RobotMoveSettingOnClicked=" + CountRobotMoveSettingOnClicked()
            + " MoveRobotControllerFound=" + (FindMoveRobotController() != null)
            + " MoveRobotActionListCount=" + MoveRobotActionListCount()
            + " MoveRobotSelectedIndex=" + MoveRobotSelectedIndex()
            + " MoveRobotIndexValid=" + MoveRobotIndexValid()
            + " MoveRobotRequiredReferences=" + MoveRobotRequiredReferences()
            + " MoveRobotUpdateYouBotTransformConfigured=" + MoveRobotUpdateYouBotTransformConfigured()
            + " MoveRobotUpdateYouBotTransformPath=" + MoveRobotUpdateYouBotTransformPath());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_CALIBRATION_BUTTON_PHOTON_JOIN_REFERENCES"
            + DescribeProtectedHandMenuPhotonJoinVerify("CalibrationSelect", "CalibrationSelect")
            + DescribeProtectedHandMenuPhotonJoinVerify("CalibrationOperation", "CalibrationOperation")
            + DescribeProtectedHandMenuPhotonJoinVerify("OriginSet", "Origin Set")
            + DescribeProtectedHandMenuPhotonJoinVerify("RobotMoveSetting", RobotMoveSettingName)
            + DescribeProtectedHandMenuPhotonJoinVerify("RobotMoveAction", RobotMoveActionName)
            + " CalibrationButtonsPhotonJoinRefs=" + CountPhotonJoinCallbacksOnProtectedHandMenuItems());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_MISSING_REFERENCES"
            + " MissingScripts=" + CountMissingScriptsInScene()
            + " MissingButtonOnClickTargets=" + CountMissingButtonOnClickTargets()
            + " MissingStatefulInteractableOnClickedTargets=" + CountMissingStatefulInteractableOnClickedTargets());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_MISSING_REFERENCES_DETAIL "
            + GetMissingUnityEventTargetDetails(12));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY poseSync"
            + " bottle=" + SharedObjectPoseSyncEnabled("NetworkedBottleProxy")
            + " box=" + SharedObjectPoseSyncEnabled("NetworkedBoxProxy")
            + " obstacle=" + SharedObjectPoseSyncEnabled("NetworkedObstacleProxy"));

#if FUSION_WEAVER && FUSION2
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY networkRunner="
            + HasComponent<NetworkRunner>("PhotonSharedRoomBootstrap"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_COUNTS_FUSION"
            + " NetworkRunner=" + CountComponentsInScene<NetworkRunner>()
            + " NetworkObjectScene=" + CountComponentsInScene<NetworkObject>()
            + " NetworkObjectAvatarPrefab=" + PrefabComponentCount<NetworkObject>(AvatarPrefabPath)
            + " NetworkObjectPhotonSharedBottlePrefab=" + PrefabComponentCount<NetworkObject>(PhotonSharedBottlePrefabPath));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY avatarPrefab NetworkObject="
            + PrefabHasComponent<NetworkObject>(AvatarPrefabPath)
            + " NetworkUserAvatar=" + PrefabHasComponent<NetworkUserAvatar>(AvatarPrefabPath)
            + " HmdOverheadCursor=" + PrefabHasChildComponent<HmdOverheadCursor>(AvatarPrefabPath)
            + " ObserverPositionCursor=" + PrefabHasChildComponent<ObserverPositionCursor>(AvatarPrefabPath)
            + " registered=" + NetworkProjectConfigUtilities.TryGetPrefabId(AvatarPrefabPath, out NetworkPrefabId avatarPrefabId)
            + " prefabId=" + avatarPrefabId);
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_PHOTON_OBSERVER_BOTTLE_AND_FINGERTIPS"
            + " ObserverBottleInteractionAllowed=" + ObserverBottleInteractionAllowed()
            + " PCEditorObserverBottleInteractionAllowed=" + PCEditorObserverBottleInteractionAllowed()
            + " BottleGrabHasNoRoleRestriction=" + BottleGrabHasNoRoleRestriction()
            + " BottleGrabHasNoRobotTargetRestriction=" + BottleGrabHasNoRobotTargetRestriction()
            + " BottlePosePublishRequiresLockOwnerOnly=" + BottlePosePublishRequiresLockOwnerOnly()
            + " ObserverPositionCursorPrefabAssigned=" + ObserverPositionCursorPrefabAssigned()
            + " ObserverPositionCursorWhite=" + ObserverPositionCursorWhite()
            + " ObserverPositionCursorSynced=" + ObserverPositionCursorSynced()
            + " ObserverLocalCursorHidden=" + ObserverLocalCursorHidden()
            + " ObserverRemoteCursorVisible=" + ObserverRemoteCursorVisible()
            + " NonObserverCursorBehaviorUnchanged=" + NonObserverCursorBehaviorUnchanged()
            + " NetworkUserAvatarFingerTipCount=" + NetworkUserAvatar.NetworkUserAvatarFingerTipCount
            + " PhotonFingerTipNetworkedFieldsPresent=" + PhotonFingerTipNetworkedFieldsPresent()
            + " PhotonFingerTipRemoteVisualsPresent=" + PhotonFingerTipRemoteVisualsPresent()
            + " PhotonFingerTipLocalVisualsHidden=" + PhotonFingerTipLocalVisualsHidden()
            + " PhotonFingerTipPcObserverDisabled=" + PhotonFingerTipPcObserverDisabled()
            + " PhotonFingerTipNoRuntimeInstantiateLoop=" + PhotonFingerTipNoRuntimeInstantiateLoop()
            + " NetworkUserAvatarSpawnGuard=" + NetworkUserAvatarSpawnGuard()
            + " HmdOverheadCursorDoesNotReadNetworkedInOnEnable=" + HmdOverheadCursorDoesNotReadNetworkedInOnEnable()
            + " ObserverPositionCursorDoesNotReadNetworkedInOnEnable=" + ObserverPositionCursorDoesNotReadNetworkedInOnEnable()
            + " FingerTipVisualsDoNotReadNetworkedBeforeSpawned=" + FingerTipVisualsDoNotReadNetworkedBeforeSpawned()
            + " AvatarNetworkedPropertiesSafeBeforeSpawned=" + AvatarNetworkedPropertiesSafeBeforeSpawned()
            + " FingerTipCursorObjectsCreated=" + FingerTipCursorObjectsCreated()
            + " FingerTipCursorRendererAssigned=" + FingerTipCursorRendererAssigned()
            + " FingerTipCursorLayerVisible=" + FingerTipCursorLayerVisible()
            + " FingerTipCursorScaleMeters=" + FingerTipCursorScaleMeters()
            + " FingerTipReadOnlyAfterSpawned=" + FingerTipReadOnlyAfterSpawned()
            + " FingerTipLocalTrackingSourceConfigured=" + FingerTipLocalTrackingSourceConfigured()
            + " FingerTipNetworkedWritePathConfigured=" + FingerTipNetworkedWritePathConfigured()
            + " FingerTipRemoteApplyPathConfigured=" + FingerTipRemoteApplyPathConfigured()
            + " FingerTipLocalAvatarHidden=" + FingerTipLocalAvatarHidden()
            + " FingerTipPcObserverHidden=" + FingerTipPcObserverHidden()
            + " PcObserverMainCameraPoseSource=" + PcObserverMainCameraPoseSource()
            + " PcObserverHeadPoseNetworkWriteConfigured=" + PcObserverHeadPoseNetworkWriteConfigured()
            + " ObserverPositionCursorUsesHeadWorldPosition=" + ObserverPositionCursorUsesHeadWorldPosition()
            + " ObserverPositionCursorRemoteOnly=" + ObserverPositionCursorRemoteOnly()
            + " BottleUsesHostAuthoritativeGrab=" + BottleUsesHostAuthoritativeGrab()
            + " BottleClientDoesNotRequireStateAuthority=" + BottleClientDoesNotRequireStateAuthority()
            + " BottlePoseWrittenOnlyByHost=" + BottlePoseWrittenOnlyByHost()
            + " BottleGrabAllowedForObserver=" + BottleGrabAllowedForObserver()
            + " PcBottleMouseUsesHostGrabRpc=" + PcBottleMouseUsesHostGrabRpc()
            + " QuestBottleGrabUsesHostGrabRpc=" + QuestBottleGrabUsesHostGrabRpc()
            + " PcBottleMouseInteractionEnabled=" + PcBottleMouseInteractionEnabled()
            + " PcBottleMouseDragUsesLockOwner=" + PcBottleMouseDragUsesLockOwner()
            + " PcBottleMouseReleaseConfigured=" + PcBottleMouseReleaseConfigured()
            + " PcBottleInputUpdateConfigured=" + PcBottleInputUpdateConfigured()
            + " PcBottleInputDoesNotRequireQuestHandTracking=" + PcBottleInputDoesNotRequireQuestHandTracking()
            + " PcBottleInputDoesNotRequireSupervisorFalse=" + PcBottleInputDoesNotRequireSupervisorFalse()
            + " PcBottleRaycastMaskIncludesSharedBottle=" + PcBottleRaycastMaskIncludesSharedBottle()
            + " PcBottleColliderEnabled=" + PcBottleColliderEnabled()
            + " PcBottleGrabRequestReachable=" + PcBottleGrabRequestReachable()
            + " PcBottleGrabPermissionWaitConfigured=" + PcBottleGrabPermissionWaitConfigured()
            + " AvatarPoseSamplingRestrictedToLocalAvatar=" + AvatarPoseSamplingRestrictedToLocalAvatar()
            + " RemoteAvatarDoesNotUseCameraMain=" + RemoteAvatarDoesNotUseCameraMain()
            + " RemoteAvatarDoesNotUseHandsAggregator=" + RemoteAvatarDoesNotUseHandsAggregator()
            + " PcObserverCameraPoseRestrictedToLocalAvatar=" + PcObserverCameraPoseRestrictedToLocalAvatar()
            + " FingerTipNetworkWriteRestrictedToLocalAvatar=" + FingerTipNetworkWriteRestrictedToLocalAvatar()
            + " FingerTipRemoteVisualsUseNetworkedValuesOnly=" + FingerTipRemoteVisualsUseNetworkedValuesOnly()
            + " EachAvatarHasIndependentPoseSource=" + EachAvatarHasIndependentPoseSource()
            + " NetworkTransform=" + PhotonSharedBottlePrefabHasNetworkTransform());
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY photonSharedBottlePrefab source="
            + PrehubBottlePrefabPath
            + " path=" + PhotonSharedBottlePrefabPath
            + " legacyCandidate=" + LegacyNetworkBottlePrefabPath
            + " NetworkObject=" + PrefabHasComponent<NetworkObject>(PhotonSharedBottlePrefabPath)
            + " NetworkedSharedSceneObject=" + PrefabHasComponent<NetworkedSharedSceneObject>(PhotonSharedBottlePrefabPath)
            + " Renderer=" + PrefabHasChildComponent<Renderer>(PhotonSharedBottlePrefabPath)
            + " Collider=" + PrefabHasChildComponent<Collider>(PhotonSharedBottlePrefabPath)
            + " Rigidbody=" + PrefabHasComponent<Rigidbody>(PhotonSharedBottlePrefabPath)
            + " NetworkTransform=" + PrefabHasComponent<NetworkTransform>(PhotonSharedBottlePrefabPath)
            + " registered=" + NetworkProjectConfigUtilities.TryGetPrefabId(PhotonSharedBottlePrefabPath, out NetworkPrefabId bottlePrefabId)
            + " prefabId=" + bottlePrefabId
            + " layer=" + PrefabRootLayerName(PhotonSharedBottlePrefabPath));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY NetworkObject bottle="
            + HasComponent<NetworkObject>("NetworkedBottleProxy")
            + " box=" + HasComponent<NetworkObject>("NetworkedBoxProxy")
            + " obstacle=" + HasComponent<NetworkObject>("NetworkedObstacleProxy"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY NetworkTransform bottle="
            + HasComponent<NetworkTransform>("NetworkedBottleProxy")
            + " box=" + HasComponent<NetworkTransform>("NetworkedBoxProxy")
            + " obstacle=" + HasComponent<NetworkTransform>("NetworkedObstacleProxy"));
#else
        Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] VERIFY Fusion defines are not active.");
#endif
    }

    private static GameObject EnsureAvatarPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
        if (prefab != null)
        {
            bool changed = false;
            GameObject contents = PrefabUtility.LoadPrefabContents(AvatarPrefabPath);
            NetworkUserAvatar avatarComponent = EnsureComponent<NetworkUserAvatar>(contents);
            changed |= EnsureHmdOverheadCursor(contents, avatarComponent);
            changed |= EnsureObserverPositionCursor(contents, avatarComponent);
#if FUSION_WEAVER && FUSION2
            if (contents.GetComponent<NetworkObject>() == null)
            {
                contents.AddComponent<NetworkObject>();
                changed = true;
            }
#endif
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(contents, AvatarPrefabPath);
            }

            PrefabUtility.UnloadPrefabContents(contents);
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
            return prefab;
        }

        GameObject avatar = new GameObject("NetworkUserAvatar");
        NetworkUserAvatar networkUserAvatar = EnsureComponent<NetworkUserAvatar>(avatar);
        EnsureHmdOverheadCursor(avatar, networkUserAvatar);
        EnsureObserverPositionCursor(avatar, networkUserAvatar);
#if FUSION_WEAVER && FUSION2
        EnsureComponent<NetworkObject>(avatar);
#endif
        prefab = PrefabUtility.SaveAsPrefabAsset(avatar, AvatarPrefabPath);
        Object.DestroyImmediate(avatar);
        return prefab;
    }

    private static GameObject EnsurePhotonSharedBottlePrefab()
    {
        Directory.CreateDirectory(PrefabFolder);
        int sharedBottleLayer = EnsureLayer(SharedBottleLayerName);

        GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrehubBottlePrefabPath);
        if (sourcePrefab == null)
        {
            Debug.LogError("[SharePhotonSharedMRSceneConfigurator] PREHUB bottle prefab was not found: " + PrehubBottlePrefabPath);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PhotonSharedBottlePrefabPath);
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(PrehubBottlePrefabPath);
        contents.name = "PhotonSharedBottle_PREHUB_bottle1";
        contents.tag = "Untagged";
        SetLayerRecursively(contents, sharedBottleLayer >= 0 ? sharedBottleLayer : 0);

        if (contents.GetComponentInChildren<Collider>(true) == null)
        {
            CapsuleCollider collider = EnsureComponent<CapsuleCollider>(contents);
            collider.radius = 0.05f;
            collider.height = 0.3f;
            collider.direction = 1;
        }

        Rigidbody rigidbody = EnsureComponent<Rigidbody>(contents);
        rigidbody.isKinematic = true;
        rigidbody.useGravity = false;

        NetworkedSharedSceneObject sharedObject = EnsureComponent<NetworkedSharedSceneObject>(contents);
        sharedObject.objectKind = SharedNetworkObjectKind.Bottle;
        sharedObject.allowStateAuthorityGrab = true;
        sharedObject.allowMouseEditorGrab = true;
        sharedObject.syncPose = true;
        sharedObject.poseSendPositionThreshold = 0.0025f;
        sharedObject.poseSendRotationThresholdDegrees = 0.5f;
        sharedObject.poseLogIntervalSeconds = 0.25f;
        sharedObject.isPhotonSharedNetworkBottle = true;

        PhotonSharedBottleVisualController visualController = EnsureComponent<PhotonSharedBottleVisualController>(contents);
        visualController.sharedObject = sharedObject;
        visualController.normalTint = Color.white;
        visualController.localGrabTint = new Color(1.0f, 0.72f, 0.18f, 1f);
        visualController.remoteGrabTint = new Color(0.22f, 0.22f, 0.22f, 1f);
        visualController.staleTint = new Color(0.36f, 0.44f, 0.50f, 1f);
        visualController.enableDebugLogs = false;

        if (!EnsureOptionalComponent(contents,
            "ObjectManipulator",
            "MixedReality.Toolkit.SpatialManipulation.ObjectManipulator",
            "Microsoft.MixedReality.Toolkit.UI.ObjectManipulator",
            "Microsoft.MixedReality.Toolkit.Input.ObjectManipulator"))
        {
            EnsureOptionalComponent(contents,
                "XRGrabInteractable",
                "UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable");
        }

        EnsureOptionalComponent(contents,
            "NearInteractionGrabbable",
            "MixedReality.Toolkit.Input.NearInteractionGrabbable",
            "Microsoft.MixedReality.Toolkit.Input.NearInteractionGrabbable");

#if FUSION_WEAVER && FUSION2
        EnsureComponent<NetworkObject>(contents);
        NetworkTransform networkTransform = contents.GetComponent<NetworkTransform>();
        if (networkTransform != null)
        {
            Object.DestroyImmediate(networkTransform);
        }
#endif

        PrefabUtility.SaveAsPrefabAsset(contents, PhotonSharedBottlePrefabPath);
        PrefabUtility.UnloadPrefabContents(contents);

        AssetDatabase.ImportAsset(PhotonSharedBottlePrefabPath);
        return AssetDatabase.LoadAssetAtPath<GameObject>(PhotonSharedBottlePrefabPath);
    }

    private static void ConfigureSharedObject(GameObject target, SharedNetworkObjectKind kind)
    {
        NetworkedSharedSceneObject sharedObject = EnsureComponent<NetworkedSharedSceneObject>(target);
        sharedObject.objectKind = kind;
        sharedObject.allowStateAuthorityGrab = true;
        sharedObject.allowMouseEditorGrab = kind == SharedNetworkObjectKind.Bottle || kind == SharedNetworkObjectKind.Box;
        sharedObject.syncPose = true;
        sharedObject.poseSendPositionThreshold = 0.0025f;
        sharedObject.poseSendRotationThresholdDegrees = 0.5f;
        sharedObject.poseLogIntervalSeconds = 0.25f;
#if FUSION_WEAVER && FUSION2
        EnsureComponent<NetworkObject>(target);
#endif
    }

    private static GameObject EnsureSceneObject(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        if (found != null)
        {
            return found;
        }

        return new GameObject(objectName);
    }

    private static GameObject EnsureChild(GameObject parent, string objectName)
    {
        Transform child = parent.transform.Find(objectName);
        if (child != null)
        {
            return child.gameObject;
        }

        GameObject obj = new GameObject(objectName);
        obj.transform.SetParent(parent.transform, false);
        return obj;
    }

    private static void DestroyChildIfExists(GameObject parent, string objectName)
    {
        if (parent == null)
        {
            return;
        }

        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.transform.GetChild(i);
            if (child.name == objectName)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void DestroyDuplicateChildren(GameObject parent, string objectName)
    {
        if (parent == null)
        {
            return;
        }

        bool keepOne = false;
        for (int i = parent.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.transform.GetChild(i);
            if (child.name != objectName)
            {
                continue;
            }

            if (!keepOne)
            {
                keepOne = true;
                continue;
            }

            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void EnsurePhotonSettingEntryHierarchy()
    {
        Transform entry = FindPhotonSettingEntryTransform();
        if (entry == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Photon setting entry was not found; Photon menu hierarchy cleanup was skipped.");
            return;
        }

        Transform menuContent = entry.parent;
        MoveExistingHandMenuChildrenOutOfPhotonSetting(entry, menuContent);
        EnsurePhotonSettingEntryComponents(entry.gameObject);

        RectTransform visualRoot = EnsureRectChild(entry, PhotonSettingVisualName);
        visualRoot.SetAsFirstSibling();

        RectTransform interactableRoot = EnsureRectChild(entry, PhotonSettingInteractableName);
        interactableRoot.SetSiblingIndex(Mathf.Min(1, entry.childCount - 1));
        ConfigurePhotonSettingInteractableObject(interactableRoot.gameObject);

        for (int i = entry.childCount - 1; i >= 0; i--)
        {
            Transform child = entry.GetChild(i);
            if (child == visualRoot || child == interactableRoot || child.name == "PhotonSharedMRMenuSection")
            {
                continue;
            }

            child.SetParent(visualRoot, true);
        }
    }

    private static void MoveExistingHandMenuChildrenOutOfPhotonSetting(Transform entry, Transform menuContent)
    {
        if (entry == null || menuContent == null)
        {
            return;
        }

        Transform[] children = new Transform[entry.childCount];
        for (int i = 0; i < entry.childCount; i++)
        {
            children[i] = entry.GetChild(i);
        }

        int insertIndex = entry.GetSiblingIndex() + 1;
        for (int i = 0; i < children.Length; i++)
        {
            Transform child = children[i];
            if (child == null || !IsExistingHandMenuFunctionalChild(child.name))
            {
                continue;
            }

            child.SetParent(menuContent, true);
            child.SetSiblingIndex(Mathf.Min(insertIndex, menuContent.childCount - 1));
            insertIndex++;
            Debug.Log("[SharePhotonSharedMRSceneConfigurator] Moved existing HandMenu item out of Photon setting: "
                + child.name + " -> " + GetGameObjectPath(child));
        }
    }

    private static bool IsExistingHandMenuFunctionalChild(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string normalized = objectName.Trim().ToLowerInvariant();
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized == "calibrationselect"
            || normalized == "calibrationoperation"
            || normalized == "robot move setting"
            || normalized == "robot move action"
            || normalized == "gripper set"
            || normalized.StartsWith("origin set")
            || normalized == "stop"
            || normalized == "addview"
            || normalized == "irm"
            || normalized == "guras"
            || normalized == "spawn bottle";
    }

    private static void EnsurePhotonSettingEntryComponents(GameObject entry)
    {
        if (entry == null)
        {
            return;
        }

        ConfigurePhotonSettingInteractableObject(entry);
    }

    private static void ConfigurePhotonSettingInteractableObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        BoxCollider boxCollider = EnsureComponent<BoxCollider>(target);
        boxCollider.isTrigger = true;
        boxCollider.center = Vector3.zero;
        boxCollider.size = new Vector3(0.12f, 0.045f, 0.025f);

        EnsureComponent<StatefulInteractable>(target);
        EnsureComponent<Button>(target);
    }

    private static RectTransform EnsureRectChild(Transform parent, string objectName)
    {
        Transform child = parent != null ? parent.Find(objectName) : null;
        if (child != null)
        {
            if (child.TryGetComponent(out RectTransform existingRect))
            {
                return existingRect;
            }

            Object.DestroyImmediate(child.gameObject);
        }

        GameObject obj = new GameObject(objectName, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void DisableLegacyPhotonSettingEntry()
    {
        Transform entry = FindPhotonSettingEntryTransform();
        if (entry == null)
        {
            return;
        }

        Transform menuContent = entry.parent;
        MoveExistingHandMenuChildrenOutOfPhotonSetting(entry, menuContent);
        Object.DestroyImmediate(entry.gameObject);
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] Removed legacy Photon setting entry; Photon operations now live in PhotonButtonCollection.");
    }

    private static PhotonSharedMRButtonCollection EnsurePhotonButtonCollection(
        PhotonSharedMRLoginPanel loginPanel,
        PhotonFusionSharedRoomBootstrap bootstrap,
        PhotonSharedBottleSpawner bottleSpawner,
        PhotonSharedMRDebugPanel debugPanel)
    {
        Transform sourceCollection = FindExistingButtonCollection();
        if (sourceCollection == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] ButtonCollection was not found; PhotonButtonCollection was not created.");
            return null;
        }

        Transform menuContent = sourceCollection.parent;
        if (menuContent == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] ButtonCollection has no parent; PhotonButtonCollection was not created.");
            return null;
        }

        DestroyChildIfExists(menuContent.gameObject, PhotonButtonCollectionName);

        GameObject collection = Object.Instantiate(sourceCollection.gameObject, menuContent, false);
        collection.name = PhotonButtonCollectionName;
        collection.SetActive(true);
        collection.transform.SetSiblingIndex(Mathf.Min(sourceCollection.GetSiblingIndex() + 1, menuContent.childCount - 1));

        for (int i = collection.transform.childCount - 1; i >= 0; i--)
        {
            Object.DestroyImmediate(collection.transform.GetChild(i).gameObject);
        }

        Transform template = FindMrtkButtonTemplate(sourceCollection);
        if (template == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Pressable Button template was not found under ButtonCollection.");
        }

        Button connectionButton = ClonePhotonButton(template, collection.transform, PhotonConnectionButtonName, "Open Login");
        TMP_Text statusText = CreatePhotonStatusPanel(collection.transform);
        Button spawnButton = ClonePhotonButton(template, collection.transform, SpawnSharedBottleButtonName, "Spawn Shared Bottle");
        Button despawnButton = ClonePhotonButton(template, collection.transform, DespawnLastBottleButtonName, "Despawn Last Bottle");
        Button debugToggleButton = ClonePhotonButton(template, collection.transform, DebugPanelToggleButtonName, "Debug Panel ON/OFF");
        Button leaveRoomButton = ClonePhotonButton(template, collection.transform, LeaveRoomButtonName, "Leave Room");
        Button retryButton = ClonePhotonButton(template, collection.transform, RetryButtonName, "Retry");

        PhotonSharedMRButtonCollection manager = EnsureComponent<PhotonSharedMRButtonCollection>(collection);
        manager.collectionRoot = collection;
        manager.loginPanel = loginPanel;
        manager.bootstrap = bootstrap;
        manager.bottleSpawner = bottleSpawner;
        manager.debugPanel = debugPanel;
        manager.connectionButtonObject = FindChildRecursive(collection.transform, PhotonConnectionButtonName)?.gameObject;
        manager.connectionButton = connectionButton;
        manager.statusText = statusText;
        manager.errorText = FindPhotonButtonCollectionText(collection.transform, PhotonErrorTextName);
        manager.retryButtonObject = FindChildRecursive(collection.transform, RetryButtonName)?.gameObject;
        manager.retryButton = retryButton;
        manager.spawnButtonObject = FindChildRecursive(collection.transform, SpawnSharedBottleButtonName)?.gameObject;
        manager.spawnButton = spawnButton;
        manager.despawnButtonObject = FindChildRecursive(collection.transform, DespawnLastBottleButtonName)?.gameObject;
        manager.despawnButton = despawnButton;
        manager.debugToggleButtonObject = FindChildRecursive(collection.transform, DebugPanelToggleButtonName)?.gameObject;
        manager.debugToggleButton = debugToggleButton;
        manager.leaveRoomButtonObject = FindChildRecursive(collection.transform, LeaveRoomButtonName)?.gameObject;
        manager.leaveRoomButton = leaveRoomButton;
        manager.refreshIntervalSeconds = 0.2f;
        AddPersistentListenerIfMissing(
            leaveRoomButton,
            manager,
            manager.LeaveRoom,
            nameof(PhotonSharedMRButtonCollection.LeaveRoom));
        AddPersistentStatefulListenerIfMissing(
            manager.leaveRoomButtonObject,
            manager,
            manager.LeaveRoom,
            nameof(PhotonSharedMRButtonCollection.LeaveRoom));

        EditorUtility.SetDirty(collection);
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] Created PhotonButtonCollection from "
            + GetGameObjectPath(sourceCollection)
            + " using button template "
            + (template != null ? template.name : "Missing"));
        return manager;
    }

    private static Transform FindExistingButtonCollection()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindNamedTransformExcluding(roots[i].transform, ButtonCollectionName, PhotonButtonCollectionName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindNamedTransformExcluding(Transform root, string objectName, string excludedName)
    {
        if (root == null)
        {
            return null;
        }

        if (root.name == objectName && root.name != excludedName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamedTransformExcluding(root.GetChild(i), objectName, excludedName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindMrtkButtonTemplate(Transform sourceCollection)
    {
        if (sourceCollection == null)
        {
            return null;
        }

        StatefulInteractable[] interactables = sourceCollection.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            StatefulInteractable interactable = interactables[i];
            if (interactable == null
                || interactable.transform == sourceCollection
                || interactable.gameObject.name == "puras"
                || interactable.gameObject.name == "Spawn Bottle"
                || !interactable.gameObject.activeSelf)
            {
                continue;
            }

            return interactable.transform;
        }

        Button[] buttons = sourceCollection.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null
                || button.transform == sourceCollection
                || button.gameObject.name == "puras"
                || button.gameObject.name == "Spawn Bottle"
                || !button.gameObject.activeSelf)
            {
                continue;
            }

            return button.transform;
        }

        return null;
    }

    private static Button ClonePhotonButton(Transform template, Transform parent, string objectName, string label)
    {
        if (template == null || parent == null)
        {
            return null;
        }

        GameObject buttonObject = Object.Instantiate(template.gameObject, parent, false);
        buttonObject.name = objectName;
        buttonObject.SetActive(true);
        ClearClonedButtonActions(buttonObject);
        SetButtonLabel(buttonObject, label);

        Button[] buttons = buttonObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].interactable = true;
            buttons[i].enabled = true;
        }

        StatefulInteractable[] interactables = buttonObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            interactables[i].enabled = true;
        }

        Collider[] colliders = buttonObject.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = true;
        }

        Button directButton = buttonObject.GetComponent<Button>();
        return directButton != null ? directButton : buttonObject.GetComponentInChildren<Button>(true);
    }

    private static void ClearClonedButtonActions(GameObject buttonObject)
    {
        if (buttonObject == null)
        {
            return;
        }

        Button[] buttons = buttonObject.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick = new Button.ButtonClickedEvent();
        }

        StatefulInteractable[] interactables = buttonObject.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            ReplaceStatefulOnClicked(interactables[i]);
        }
    }

    private static void ReplaceStatefulOnClicked(StatefulInteractable interactable)
    {
        if (interactable == null)
        {
            return;
        }

        FieldInfo onClickedField = typeof(StatefulInteractable).GetField(
            "<OnClicked>k__BackingField",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (onClickedField != null)
        {
            onClickedField.SetValue(interactable, new UnityEvent());
            return;
        }

        interactable.OnClicked.RemoveAllListeners();
    }

    private static void SetButtonLabel(GameObject buttonObject, string label)
    {
        if (buttonObject == null)
        {
            return;
        }

        TMP_Text[] tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < tmpTexts.Length; i++)
        {
            if (tmpTexts[i] == null || IsIconText(tmpTexts[i].transform))
            {
                continue;
            }

            tmpTexts[i].text = label;
            tmpTexts[i].enableWordWrapping = true;
        }

        Text[] uiTexts = buttonObject.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < uiTexts.Length; i++)
        {
            if (uiTexts[i] == null)
            {
                continue;
            }

            uiTexts[i].text = label;
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

    private static TMP_Text CreatePhotonStatusPanel(Transform parent)
    {
        GameObject panelObject = new GameObject(PhotonStatusPanelName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(64f, 32f);
        panelRect.localScale = Vector3.one;

        Image panelImage = panelObject.GetComponent<Image>();
        panelImage.raycastTarget = false;
        panelImage.color = new Color(0.06f, 0.08f, 0.1f, 0.78f);

        TMP_Text statusText = CreatePhotonStatusText(panelObject.transform, PhotonStatusTextName, "Photon Connection\nStatus: Not Connected", new Vector2(0f, 3f), new Vector2(62f, 22f), 3);
        CreatePhotonStatusText(panelObject.transform, PhotonErrorTextName, string.Empty, new Vector2(0f, -12f), new Vector2(62f, 8f), 2);
        return statusText;
    }

    private static TMP_Text CreatePhotonStatusText(Transform parent, string objectName, string text, Vector2 position, Vector2 size, int fontSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    private static TMP_Text FindPhotonButtonCollectionText(Transform collection, string objectName)
    {
        Transform textTransform = FindChildRecursive(collection, objectName);
        return textTransform != null ? textTransform.GetComponent<TMP_Text>() : null;
    }

    private static PhotonSharedMRHandMenuSpawnBottleBridge EnsureHandMenuSpawnBottleBridge(
        GameObject root,
        PhotonFusionSharedRoomBootstrap bootstrap,
        PhotonSharedBottleSpawner bottleSpawner,
        PhotonDetectedBottleBridge detectedBottleBridge)
    {
        if (root == null)
        {
            return null;
        }

        GameObject bridgeObject = EnsureChild(root, HandMenuSpawnBottleBridgeName);
        PhotonSharedMRHandMenuSpawnBottleBridge bridge = EnsureComponent<PhotonSharedMRHandMenuSpawnBottleBridge>(bridgeObject);
        bridge.bootstrap = bootstrap;
        bridge.sharedBottleSpawner = bottleSpawner;
        bridge.localBottleSpawner = Object.FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        bridge.detectedBottleBridge = detectedBottleBridge;

        Transform spawnBottle = FindHandMenuSpawnBottleTransform();
        bridge.handMenuSpawnBottleObject = spawnBottle != null ? spawnBottle.gameObject : null;
        ConfigureHandMenuSpawnBottleAction(spawnBottle, bridge);
        EditorUtility.SetDirty(bridgeObject);
        return bridge;
    }

    private static PhotonDetectedBottleBridge EnsurePhotonDetectedBottleBridge(
        GameObject root,
        PhotonFusionSharedRoomBootstrap bootstrap,
        PhotonSharedBottleSpawner bottleSpawner)
    {
        if (root == null)
        {
            return null;
        }

        GameObject bridgeObject = EnsureChild(root, PhotonDetectedBottleBridgeName);
        PhotonDetectedBottleBridge bridge = EnsureComponent<PhotonDetectedBottleBridge>(bridgeObject);
        bridge.detectedBottleSubscriber = Object.FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        bridge.sharedBottleSpawner = bottleSpawner;
        bridge.bootstrap = bootstrap;
        bridge.enableBridge = true;
        bridge.authorityMode = PhotonDetectionAuthorityMode.LocalRealSenseAuthority;
        bridge.localRealSenseAuthorityEnabled = true;
        bridge.validDetectionMaxAgeSeconds = 0.75f;
        bridge.lostDetectionAgeSeconds = 3.0f;
        bridge.sharedPoseUpdateMinIntervalSeconds = 0.05f;
        bridge.spawnRetryIntervalSeconds = 1.0f;
        bridge.enableDebugLogs = false;
        bridge.poseUpdateLogIntervalSeconds = 0.5f;
        bridge.stateLogIntervalSeconds = 2.0f;
        EditorUtility.SetDirty(bridgeObject);
        return bridge;
    }

    private static Transform FindHandMenuSpawnBottleTransform()
    {
        Transform menuContent = FindMenuContentTransform();
        if (menuContent == null)
        {
            return null;
        }

        Transform directChild = menuContent.Find(HandMenuSpawnBottleName);
        return directChild != null ? directChild : FindChildRecursive(menuContent, HandMenuSpawnBottleName);
    }

    private static string GetHandMenuSpawnBottlePath()
    {
        Transform spawnBottle = FindHandMenuSpawnBottleTransform();
        return spawnBottle != null ? GetGameObjectPath(spawnBottle) : "Missing";
    }

    private static void ConfigureHandMenuSpawnBottleAction(
        Transform spawnBottle,
        PhotonSharedMRHandMenuSpawnBottleBridge bridge)
    {
        if (spawnBottle == null || bridge == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] HandMenu Spawn Bottle button or bridge was not found; bridge wiring skipped.");
            return;
        }

        Button[] buttons = spawnBottle.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick = new Button.ButtonClickedEvent();
            UnityEventTools.AddPersistentListener(buttons[i].onClick, bridge.SpawnBottleFromHandMenu);
            EditorUtility.SetDirty(buttons[i]);
        }

        StatefulInteractable[] interactables = spawnBottle.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            ReplaceStatefulOnClicked(interactables[i]);
            UnityEventTools.AddPersistentListener(interactables[i].OnClicked, bridge.SpawnBottleFromHandMenu);
            EditorUtility.SetDirty(interactables[i]);
        }

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] Wired HandMenu Spawn Bottle to Photon/local bridge: "
            + GetGameObjectPath(spawnBottle));
    }

    private static void EnsureStartupLoginPanelPersistentWiring(PhotonSharedMRLoginPanel loginPanel)
    {
        if (loginPanel == null)
        {
            return;
        }

        AddPersistentListenerIfMissing(loginPanel.startButton, loginPanel, loginPanel.StartSessionFromUi, nameof(PhotonSharedMRLoginPanel.StartSessionFromUi));
        AddPersistentListenerIfMissing(loginPanel.amirRobotButton, loginPanel, loginPanel.SelectAmirRobot, nameof(PhotonSharedMRLoginPanel.SelectAmirRobot));
        AddPersistentListenerIfMissing(loginPanel.roverRobotButton, loginPanel, loginPanel.SelectRoverRobot, nameof(PhotonSharedMRLoginPanel.SelectRoverRobot));
        AddPersistentListenerIfMissing(loginPanel.droneRobotButton, loginPanel, loginPanel.SelectDroneRobot, nameof(PhotonSharedMRLoginPanel.SelectDroneRobot));
        AddPersistentListenerIfMissing(loginPanel.observerRobotButton, loginPanel, loginPanel.SelectObserverRobot, nameof(PhotonSharedMRLoginPanel.SelectObserverRobot));
    }

    private static void AddPersistentListenerIfMissing(Button button, Object target, UnityAction action, string methodName)
    {
        if (button == null || target == null || action == null)
        {
            return;
        }

        if (CountPersistentMethodCalls(button.onClick, methodName) == 0)
        {
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }
    }

    private static void AddPersistentStatefulListenerIfMissing(GameObject root, Object target, UnityAction action, string methodName)
    {
        if (root == null || target == null || action == null)
        {
            return;
        }

        StatefulInteractable[] interactables = root.GetComponentsInChildren<StatefulInteractable>(true);
        if (interactables.Length == 0)
        {
            return;
        }

        for (int i = 0; i < interactables.Length; i++)
        {
            if (CountPersistentMethodCalls(interactables[i].OnClicked, methodName) > 0)
            {
                return;
            }
        }

        UnityEventTools.AddPersistentListener(interactables[0].OnClicked, action);
        EditorUtility.SetDirty(interactables[0]);
    }

    private static void EnsureRobotMoveButtonWiring()
    {
        MoveRobotController controller = FindMoveRobotController();
        if (controller == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] MoveRobotController was not found; Robot Move button wiring skipped.");
            return;
        }

        EnsureUpdateYouBotTransformReference(controller);
        controller.ResolveMissingReferencesForScene();
        ConfigureSingleHandMenuAction(
            RobotMoveSettingName,
            controller.PressRobotMoveSettingButton,
            nameof(MoveRobotController.PressRobotMoveSettingButton));
        ConfigureSingleHandMenuAction(
            RobotMoveActionName,
            controller.PressRobotMoveActionButton,
            nameof(MoveRobotController.PressRobotMoveActionButton));
        EditorUtility.SetDirty(controller);
    }

    private static void EnsureUpdateYouBotTransformReference(MoveRobotController controller)
    {
        if (controller == null)
        {
            return;
        }

        SerializedObject controllerObject = new SerializedObject(controller);
        SerializedProperty youbotProperty = controllerObject.FindProperty("YoubotObject");
        SerializedProperty updateProperty = controllerObject.FindProperty("updateYouBotTransform");
        GameObject youbotObject = youbotProperty != null ? youbotProperty.objectReferenceValue as GameObject : null;
        UpdateYouBotTransform updateTransform = updateProperty != null
            ? updateProperty.objectReferenceValue as UpdateYouBotTransform
            : null;

        if (updateTransform == null)
        {
            updateTransform = Object.FindObjectOfType<UpdateYouBotTransform>(true);
        }

        if (updateTransform == null)
        {
            GameObject host = youbotObject != null ? youbotObject : controller.gameObject;
            updateTransform = host.GetComponent<UpdateYouBotTransform>();
            if (updateTransform == null)
            {
                updateTransform = host.AddComponent<UpdateYouBotTransform>();
                Debug.Log("[SharePhotonSharedMRSceneConfigurator] Added UpdateYouBotTransform for Robot Move Action host="
                    + GetGameObjectPath(host.transform));
            }
        }

        YouBotPosSubscriber youBotPosSubscriber = Object.FindObjectOfType<YouBotPosSubscriber>(true);
        SerializedObject updateObject = new SerializedObject(updateTransform);
        SerializedProperty subscriberProperty = updateObject.FindProperty("youBotPosSubscriber");
        SerializedProperty updateYoubotProperty = updateObject.FindProperty("YoubotObject");
        if (subscriberProperty != null)
        {
            subscriberProperty.objectReferenceValue = youBotPosSubscriber;
        }

        if (updateYoubotProperty != null)
        {
            updateYoubotProperty.objectReferenceValue = youbotObject;
        }

        updateObject.ApplyModifiedProperties();

        if (updateProperty != null)
        {
            updateProperty.objectReferenceValue = updateTransform;
            controllerObject.ApplyModifiedProperties();
        }

        OperationStatusController operationStatus = Object.FindObjectOfType<OperationStatusController>(true);
        if (operationStatus != null)
        {
            SerializedObject operationStatusObject = new SerializedObject(operationStatus);
            SerializedProperty operationUpdateProperty = operationStatusObject.FindProperty("updateYouBotTransform");
            if (operationUpdateProperty != null)
            {
                operationUpdateProperty.objectReferenceValue = updateTransform;
                operationStatusObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(operationStatus);
            }
        }

        EditorUtility.SetDirty(updateTransform);
        EditorUtility.SetDirty(controller);
    }

    private static void ConfigureSingleHandMenuAction(string itemName, UnityAction action, string methodName)
    {
        Transform item = FindHandMenuMenuItemTransform(itemName);
        if (item == null || action == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] HandMenu item was not found for action wiring: " + itemName);
            return;
        }

        Button[] buttons = item.GetComponentsInChildren<Button>(true);
        StatefulInteractable[] interactables = item.GetComponentsInChildren<StatefulInteractable>(true);

        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].onClick = new Button.ButtonClickedEvent();
            EditorUtility.SetDirty(buttons[i]);
        }

        StatefulInteractable selectedInteractable = SelectPrimaryStatefulInteractable(item, interactables);
        for (int i = 0; i < interactables.Length; i++)
        {
            ReplaceStatefulOnClicked(interactables[i]);
            EditorUtility.SetDirty(interactables[i]);
        }

        if (selectedInteractable != null)
        {
            UnityEventTools.AddPersistentListener(selectedInteractable.OnClicked, action);
            EditorUtility.SetDirty(selectedInteractable);
        }
        else if (buttons.Length > 0)
        {
            UnityEventTools.AddPersistentListener(buttons[0].onClick, action);
            EditorUtility.SetDirty(buttons[0]);
        }

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] Wired " + itemName
            + " to " + methodName
            + " path=" + GetGameObjectPath(item)
            + " statefulCount=" + interactables.Length
            + " buttonCount=" + buttons.Length);
    }

    private static StatefulInteractable SelectPrimaryStatefulInteractable(Transform item, StatefulInteractable[] interactables)
    {
        if (item == null || interactables == null || interactables.Length == 0)
        {
            return null;
        }

        StatefulInteractable direct = item.GetComponent<StatefulInteractable>();
        if (direct != null)
        {
            return direct;
        }

        for (int i = 0; i < interactables.Length; i++)
        {
            if (interactables[i] != null && interactables[i].gameObject.activeInHierarchy)
            {
                return interactables[i];
            }
        }

        return interactables[0];
    }

    private static void RemovePhotonJoinCallbacksFromProtectedHandMenuItems()
    {
        int totalRemoved = 0;
        for (int i = 0; i < PhotonJoinProtectedHandMenuItems.Length; i++)
        {
            totalRemoved += RemovePhotonJoinCallbacksFromHandMenuItem(PhotonJoinProtectedHandMenuItems[i]);
        }

        if (totalRemoved > 0)
        {
            Debug.Log("[SharePhotonSharedMRSceneConfigurator] Removed Photon join callbacks from protected HandMenu items count="
                + totalRemoved);
        }
    }

    private static int RemovePhotonJoinCallbacksFromHandMenuItem(string itemName)
    {
        List<Transform> items = FindProtectedHandMenuItemTransforms(itemName);
        int removed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null)
            {
                continue;
            }

            Button[] buttons = item.GetComponentsInChildren<Button>(true);
            for (int buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
            {
                removed += RemovePhotonJoinCallbacks(
                    buttons[buttonIndex].onClick,
                    buttons[buttonIndex],
                    itemName,
                    "Button.onClick");
            }

            StatefulInteractable[] interactables = item.GetComponentsInChildren<StatefulInteractable>(true);
            for (int interactableIndex = 0; interactableIndex < interactables.Length; interactableIndex++)
            {
                removed += RemovePhotonJoinCallbacks(
                    interactables[interactableIndex].OnClicked,
                    interactables[interactableIndex],
                    itemName,
                    "StatefulInteractable.OnClicked");
            }
        }

        return removed;
    }

    private static int RemovePhotonJoinCallbacks(
        UnityEventBase unityEvent,
        Object eventOwner,
        string itemName,
        string eventName)
    {
        if (unityEvent == null)
        {
            return 0;
        }

        int removed = 0;
        for (int i = unityEvent.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            if (!IsPhotonJoinCallback(unityEvent, i))
            {
                continue;
            }

            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Removing Photon join callback from protected HandMenu item"
                + " item=" + itemName
                + " event=" + eventName
                + " target=" + DescribePersistentTarget(unityEvent, i)
                + " method=" + unityEvent.GetPersistentMethodName(i));
            UnityEventTools.RemovePersistentListener(unityEvent, i);
            removed++;
        }

        if (removed > 0 && eventOwner != null)
        {
            EditorUtility.SetDirty(eventOwner);
        }

        return removed;
    }

    private static int CountPhotonJoinCallbacksOnProtectedHandMenuItems()
    {
        int count = 0;
        for (int i = 0; i < PhotonJoinProtectedHandMenuItems.Length; i++)
        {
            count += CountPhotonJoinCallbacksOnHandMenuItem(PhotonJoinProtectedHandMenuItems[i]);
        }

        return count;
    }

    private static string DescribeProtectedHandMenuPhotonJoinVerify(string label, string itemName)
    {
        List<Transform> items = FindProtectedHandMenuItemTransforms(itemName);
        return " " + label + "Found=" + (items.Count > 0)
            + " " + label + "Hierarchy=" + FormatTransformPaths(items)
            + " " + label + "StatefulInteractable=" + HasStatefulInteractable(items)
            + " " + label + "OnClickedCount=" + CountStatefulOnClickedPersistentCalls(items)
            + " " + label + "ButtonOnClickCount=" + CountButtonOnClickPersistentCalls(items)
            + " " + label + "PhotonJoinRefs=" + CountPhotonJoinCallbacks(items);
    }

    private static int CountPhotonJoinCallbacksOnHandMenuItem(string itemName)
    {
        return CountPhotonJoinCallbacks(FindProtectedHandMenuItemTransforms(itemName));
    }

    private static int CountPhotonJoinCallbacks(List<Transform> items)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null)
            {
                continue;
            }

            Button[] buttons = item.GetComponentsInChildren<Button>(true);
            for (int buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
            {
                count += CountPhotonJoinCallbacks(buttons[buttonIndex].onClick);
            }

            StatefulInteractable[] interactables = item.GetComponentsInChildren<StatefulInteractable>(true);
            for (int interactableIndex = 0; interactableIndex < interactables.Length; interactableIndex++)
            {
                count += CountPhotonJoinCallbacks(interactables[interactableIndex].OnClicked);
            }
        }

        return count;
    }

    private static bool HasStatefulInteractable(List<Transform> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item != null && item.GetComponentsInChildren<StatefulInteractable>(true).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountStatefulOnClickedPersistentCalls(List<Transform> items)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null)
            {
                continue;
            }

            StatefulInteractable[] interactables = item.GetComponentsInChildren<StatefulInteractable>(true);
            for (int interactableIndex = 0; interactableIndex < interactables.Length; interactableIndex++)
            {
                count += interactables[interactableIndex].OnClicked != null
                    ? interactables[interactableIndex].OnClicked.GetPersistentEventCount()
                    : 0;
            }
        }

        return count;
    }

    private static int CountButtonOnClickPersistentCalls(List<Transform> items)
    {
        int count = 0;
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null)
            {
                continue;
            }

            Button[] buttons = item.GetComponentsInChildren<Button>(true);
            for (int buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
            {
                count += buttons[buttonIndex].onClick != null
                    ? buttons[buttonIndex].onClick.GetPersistentEventCount()
                    : 0;
            }
        }

        return count;
    }

    private static int CountPhotonJoinCallbacks(UnityEventBase unityEvent)
    {
        if (unityEvent == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (IsPhotonJoinCallback(unityEvent, i))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsPhotonJoinCallback(UnityEventBase unityEvent, int index)
    {
        if (unityEvent == null || index < 0 || index >= unityEvent.GetPersistentEventCount())
        {
            return false;
        }

        Object target = unityEvent.GetPersistentTarget(index);
        string methodName = unityEvent.GetPersistentMethodName(index);
        if (target == null || string.IsNullOrWhiteSpace(methodName))
        {
            return false;
        }

        string typeName = target.GetType().Name;
        bool photonTarget = typeName == nameof(PhotonSharedMRLoginPanel)
            || typeName == nameof(PhotonFusionSharedRoomBootstrap)
            || typeName == nameof(PhotonSharedMRMenuPanel)
            || typeName == nameof(PhotonSharedMRButtonCollection);
        if (!photonTarget)
        {
            return false;
        }

        return methodName.Contains("StartSession")
            || methodName.Contains("StartSharedRoom")
            || methodName.Contains("StartOrJoin")
            || methodName.Contains("Retry")
            || methodName.Contains("Join")
            || methodName.Contains("OpenLogin");
    }

    private static string DescribePersistentTarget(UnityEventBase unityEvent, int index)
    {
        Object target = unityEvent != null ? unityEvent.GetPersistentTarget(index) : null;
        if (target == null)
        {
            return "Missing";
        }

        Component component = target as Component;
        if (component != null)
        {
            return target.GetType().Name + "@" + GetGameObjectPath(component.transform);
        }

        GameObject gameObject = target as GameObject;
        if (gameObject != null)
        {
            return target.GetType().Name + "@" + GetGameObjectPath(gameObject.transform);
        }

        return target.GetType().Name;
    }

    private static List<Transform> FindProtectedHandMenuItemTransforms(string itemName)
    {
        List<Transform> results = new List<Transform>();
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            AddMatchingProtectedHandMenuItems(roots[i].transform, itemName, results);
        }

        return results;
    }

    private static void AddMatchingProtectedHandMenuItems(Transform root, string itemName, List<Transform> results)
    {
        if (root == null || results == null)
        {
            return;
        }

        if (IsProtectedHandMenuItemNameMatch(root.name, itemName))
        {
            results.Add(root);
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            AddMatchingProtectedHandMenuItems(root.GetChild(i), itemName, results);
        }
    }

    private static bool IsProtectedHandMenuItemNameMatch(string candidateName, string itemName)
    {
        string candidate = NormalizeHandMenuItemName(candidateName);
        string expected = NormalizeHandMenuItemName(itemName);
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        if (expected == "origin set")
        {
            return candidate.StartsWith("origin set");
        }

        return candidate == expected;
    }

    private static string NormalizeHandMenuItemName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim().ToLowerInvariant();
        while (normalized.Contains("  "))
        {
            normalized = normalized.Replace("  ", " ");
        }

        return normalized;
    }

    private static string FormatTransformPaths(List<Transform> items)
    {
        if (items == null || items.Count == 0)
        {
            return "Missing";
        }

        string result = string.Empty;
        for (int i = 0; i < items.Count; i++)
        {
            Transform item = items[i];
            if (item == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(result))
            {
                result += "|";
            }

            result += GetGameObjectPath(item);
        }

        return string.IsNullOrEmpty(result) ? "Missing" : result;
    }

    private static RectTransform FindExistingMenuRoot()
    {
        Transform sourceCollection = FindExistingButtonCollection();
        if (sourceCollection != null
            && sourceCollection.parent != null
            && sourceCollection.parent.TryGetComponent(out RectTransform buttonCollectionMenuRect))
        {
            return buttonCollectionMenuRect;
        }

        RectTransform photonSettingRoot = FindPhotonSettingMenuRoot();
        if (photonSettingRoot != null)
        {
            return photonSettingRoot;
        }

        GameObject uiMenu = GameObject.Find("UiMenuPrefab");
        if (uiMenu != null && uiMenu.TryGetComponent(out RectTransform uiMenuRect))
        {
            return uiMenuRect;
        }

        GameObject handMenu = GameObject.Find("HandMenu");
        if (handMenu != null)
        {
            Transform menuContent = FindChildRecursive(handMenu.transform, "MenuContent");
            if (menuContent != null && menuContent.TryGetComponent(out RectTransform menuContentRect))
            {
                return menuContentRect;
            }

            if (handMenu.TryGetComponent(out RectTransform handMenuRect))
            {
                return handMenuRect;
            }
        }

        Canvas[] canvases = Object.FindObjectsOfType<Canvas>(true);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null
                && canvas.renderMode == RenderMode.WorldSpace
                && !IsReservedPhotonCanvas(canvas.transform))
            {
                RectTransform rect = canvas.GetComponent<RectTransform>();
                if (rect != null)
                {
                    return rect;
                }
            }
        }

        Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Existing menu root was not found; Photon menu panel will resolve it at runtime.");
        return null;
    }

    private static RectTransform FindPhotonSettingMenuRoot()
    {
        Transform entry = FindPhotonSettingEntryTransform();
        if (entry == null)
        {
            return null;
        }

        if (entry.TryGetComponent(out RectTransform entryRect))
        {
            return entryRect;
        }

        Transform parent = entry.parent;
        while (parent != null)
        {
            if (parent.name == "MenuContent" && parent.TryGetComponent(out RectTransform menuContentRect))
            {
                return menuContentRect;
            }

            parent = parent.parent;
        }

        Transform directParent = entry.parent;
        return directParent != null && directParent.TryGetComponent(out RectTransform parentRect)
            ? parentRect
            : null;
    }

    private static Transform FindPhotonSettingEntryTransform()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, "Photon setting")
                ?? FindChildRecursive(roots[i].transform, "Photon Setting");
            if (found != null)
            {
                return found;
            }
        }

        return null;
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

    private static string GetSceneObjectPath(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, objectName);
            if (found != null)
            {
                return GetGameObjectPath(found);
            }
        }

        return "Missing";
    }

    private static string GetGameObjectPath(Transform transform)
    {
        if (transform == null)
        {
            return "Missing";
        }

        string path = transform.name;
        Transform parent = transform.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }

    private static GameObject EnsurePrimitiveChild(GameObject parent, string objectName, PrimitiveType primitiveType, Vector3 localScale, Vector3 localPosition, Color color)
    {
        GameObject obj = EnsureChild(parent, objectName);
        if (obj.GetComponent<MeshFilter>() == null)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            MeshFilter sourceFilter = primitive.GetComponent<MeshFilter>();
            MeshRenderer sourceRenderer = primitive.GetComponent<MeshRenderer>();
            MeshFilter targetFilter = obj.AddComponent<MeshFilter>();
            MeshRenderer targetRenderer = obj.AddComponent<MeshRenderer>();
            targetFilter.sharedMesh = sourceFilter.sharedMesh;
            targetRenderer.sharedMaterial = CreateMaterial(color);
            Object.DestroyImmediate(primitive);
        }

        if (obj.GetComponent<Collider>() == null)
        {
            if (primitiveType == PrimitiveType.Sphere)
            {
                obj.AddComponent<SphereCollider>();
            }
            else if (primitiveType == PrimitiveType.Capsule || primitiveType == PrimitiveType.Cylinder)
            {
                obj.AddComponent<CapsuleCollider>();
            }
            else
            {
                obj.AddComponent<BoxCollider>();
            }
        }

        obj.transform.localScale = localScale;
        obj.transform.localPosition = localPosition;
        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateMaterial(color);
        }

        return obj;
    }

    private static GameObject EnsureLineChild(GameObject parent, string objectName, Vector3[] points, Color color)
    {
        GameObject obj = EnsureChild(parent, objectName);
        LineRenderer line = EnsureComponent<LineRenderer>(obj);
        line.useWorldSpace = true;
        line.widthMultiplier = 0.012f;
        line.sharedMaterial = CreateMaterial(color);
        line.positionCount = points.Length;
        for (int i = 0; i < points.Length; i++)
        {
            line.SetPosition(i, points[i]);
        }

        return obj;
    }

    private static GameObject EnsureTextChild(GameObject parent, string objectName, string text, Vector3 localPosition, Color color)
    {
        GameObject obj = EnsureChild(parent, objectName);
        TextMesh textMesh = EnsureComponent<TextMesh>(obj);
        textMesh.text = text;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.035f;
        textMesh.fontSize = 48;
        textMesh.color = color;
        obj.transform.localPosition = localPosition;
        return obj;
    }

    private static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        if (component == null)
        {
            component = target.AddComponent<T>();
        }

        return component;
    }

    private static bool EnsureHmdOverheadCursor(GameObject avatarRoot, NetworkUserAvatar avatar)
    {
        bool changed = false;
        Transform cursorTransform = avatarRoot.transform.Find("HeadOverheadCursor");
        if (cursorTransform == null)
        {
            GameObject cursorObject = new GameObject("HeadOverheadCursor");
            cursorObject.transform.SetParent(avatarRoot.transform, false);
            cursorTransform = cursorObject.transform;
            changed = true;
        }

        HmdOverheadCursor cursor = cursorTransform.GetComponent<HmdOverheadCursor>();
        if (cursor == null)
        {
            cursor = cursorTransform.gameObject.AddComponent<HmdOverheadCursor>();
            changed = true;
        }

        if (cursor.avatar != avatar)
        {
            cursor.avatar = avatar;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.cursorVerticalOffset, 0.25f))
        {
            cursor.cursorVerticalOffset = 0.25f;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.textScale, 0.12f))
        {
            cursor.textScale = 0.12f;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.minVisibleDistance, 0f))
        {
            cursor.minVisibleDistance = 0f;
            changed = true;
        }

        if (!cursor.billboardToCamera)
        {
            cursor.billboardToCamera = true;
            changed = true;
        }

        if (!cursor.showHostLikeFlag)
        {
            cursor.showHostLikeFlag = true;
            changed = true;
        }

        if (!cursor.showRobotTarget)
        {
            cursor.showRobotTarget = true;
            changed = true;
        }

        return changed;
    }

    private static bool EnsureObserverPositionCursor(GameObject avatarRoot, NetworkUserAvatar avatar)
    {
        bool changed = false;
        Transform cursorTransform = avatarRoot.transform.Find("ObserverPositionCursor");
        if (cursorTransform == null)
        {
            GameObject cursorObject = new GameObject("ObserverPositionCursor");
            cursorObject.transform.SetParent(avatarRoot.transform, false);
            cursorTransform = cursorObject.transform;
            changed = true;
        }

        ObserverPositionCursor cursor = cursorTransform.GetComponent<ObserverPositionCursor>();
        if (cursor == null)
        {
            cursor = cursorTransform.gameObject.AddComponent<ObserverPositionCursor>();
            changed = true;
        }

        if (cursor.avatar != avatar)
        {
            cursor.avatar = avatar;
            changed = true;
        }

        if (cursor.cursorColor != Color.white)
        {
            cursor.cursorColor = Color.white;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.cursorScale, 0.04f))
        {
            cursor.cursorScale = 0.04f;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.labelVerticalOffset, 0.06f))
        {
            cursor.labelVerticalOffset = 0.06f;
            changed = true;
        }

        if (!Mathf.Approximately(cursor.textScale, 0.10f))
        {
            cursor.textScale = 0.10f;
            changed = true;
        }

        if (!cursor.billboardToCamera)
        {
            cursor.billboardToCamera = true;
            changed = true;
        }

        return changed;
    }

    private static Transform FindTransform(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private static OVRHand FindRightOvrHand()
    {
        OVRHand[] hands = Object.FindObjectsOfType<OVRHand>(true);
        for (int i = 0; i < hands.Length; i++)
        {
            OVRHand hand = hands[i];
            if (hand != null && IsRightHandName(hand.name))
            {
                return hand;
            }
        }

        return null;
    }

    private static bool IsRightHandName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        string lowerName = objectName.ToLowerInvariant();
        return lowerName.Contains("right")
            || lowerName.Contains("righthand")
            || lowerName.Contains("right_hand")
            || lowerName.Contains("hand_r")
            || lowerName.EndsWith("_r")
            || lowerName.EndsWith("-r");
    }

    private static bool HasComponent<T>(string objectName) where T : Component
    {
        GameObject found = GameObject.Find(objectName);
        return found != null && found.GetComponent<T>() != null;
    }

    private static string GetPhotonSettingDirectChildNames()
    {
        Transform photonSetting = FindPhotonSettingEntryTransform();
        if (photonSetting == null)
        {
            return "Missing";
        }

        string result = string.Empty;
        for (int i = 0; i < photonSetting.childCount; i++)
        {
            if (i > 0)
            {
                result += "|";
            }

            result += photonSetting.GetChild(i).name;
        }

        return result;
    }

    private static int CountPhotonSettingNonPhotonDirectChildren()
    {
        Transform photonSetting = FindPhotonSettingEntryTransform();
        if (photonSetting == null)
        {
            return -1;
        }

        int count = 0;
        for (int i = 0; i < photonSetting.childCount; i++)
        {
            string childName = photonSetting.GetChild(i).name;
            if (childName != PhotonSettingVisualName
                && childName != PhotonSettingInteractableName
                && childName != "PhotonSharedMRMenuSection")
            {
                count++;
            }
        }

        return count;
    }

    private static int CountPhotonSettingExistingFunctionalDirectChildren()
    {
        Transform photonSetting = FindPhotonSettingEntryTransform();
        if (photonSetting == null)
        {
            return -1;
        }

        int count = 0;
        for (int i = 0; i < photonSetting.childCount; i++)
        {
            if (IsExistingHandMenuFunctionalChild(photonSetting.GetChild(i).name))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountDirectMenuContentChildrenNamed(string objectName)
    {
        Transform menuContent = FindPhotonSettingMenuContentParent();
        if (menuContent == null)
        {
            return -1;
        }

        int count = 0;
        for (int i = 0; i < menuContent.childCount; i++)
        {
            if (menuContent.GetChild(i).name == objectName)
            {
                count++;
            }
        }

        return count;
    }

    private static int CountDirectMenuContentChildrenStartingWith(string objectNamePrefix)
    {
        Transform menuContent = FindPhotonSettingMenuContentParent();
        if (menuContent == null)
        {
            return -1;
        }

        int count = 0;
        for (int i = 0; i < menuContent.childCount; i++)
        {
            if (menuContent.GetChild(i).name.Trim().StartsWith(objectNamePrefix))
            {
                count++;
            }
        }

        return count;
    }

    private static Transform FindPhotonSettingMenuContentParent()
    {
        Transform entry = FindPhotonSettingEntryTransform();
        Transform parent = entry != null ? entry.parent : null;
        while (parent != null)
        {
            if (parent.name == "MenuContent")
            {
                return parent;
            }

            parent = parent.parent;
        }

        if (entry != null)
        {
            return entry.parent;
        }

        return FindMenuContentTransform();
    }

    private static Transform FindMenuContentTransform()
    {
        Transform sourceCollection = FindExistingButtonCollection();
        if (sourceCollection != null && sourceCollection.parent != null)
        {
            return sourceCollection.parent;
        }

        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, "MenuContent");
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static string GetPhotonButtonCollectionDirectChildNames()
    {
        Transform collection = FindSceneObjectTransform(PhotonButtonCollectionName);
        if (collection == null)
        {
            return "Missing";
        }

        string result = string.Empty;
        for (int i = 0; i < collection.childCount; i++)
        {
            if (i > 0)
            {
                result += "|";
            }

            result += collection.GetChild(i).name;
        }

        return result;
    }

    private static string GetPhotonButtonTemplatePrefabPath()
    {
        Transform sourceCollection = FindExistingButtonCollection();
        Transform template = FindMrtkButtonTemplate(sourceCollection);
        if (template == null)
        {
            return "Missing";
        }

        string guidPath = AssetDatabase.GUIDToAssetPath(MrtkPressableButtonIconTextGuid);
        if (!string.IsNullOrWhiteSpace(guidPath))
        {
            return guidPath;
        }

        GameObject originalSourceObject = PrefabUtility.GetCorrespondingObjectFromOriginalSource(template.gameObject);
        string originalSourcePath = originalSourceObject != null ? AssetDatabase.GetAssetPath(originalSourceObject) : string.Empty;
        if (!string.IsNullOrWhiteSpace(originalSourcePath))
        {
            return originalSourcePath;
        }

        GameObject sourceObject = PrefabUtility.GetCorrespondingObjectFromSource(template.gameObject);
        string sourcePath = sourceObject != null ? AssetDatabase.GetAssetPath(sourceObject) : string.Empty;
        if (!string.IsNullOrWhiteSpace(sourcePath))
        {
            return sourcePath;
        }

        string nearestPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(template.gameObject);
        return string.IsNullOrWhiteSpace(nearestPath) ? template.name : nearestPath;
    }

    private static string DescribeButtonComponents(string objectName)
    {
        Transform transform = FindSceneObjectTransform(objectName);
        if (transform == null)
        {
            return "Missing";
        }

        return "Path=" + GetGameObjectPath(transform)
            + ",Active=" + transform.gameObject.activeSelf
            + ",BoxCollider=" + (transform.GetComponent<BoxCollider>() != null && transform.GetComponent<BoxCollider>().enabled)
            + ",StatefulInteractable=" + (transform.GetComponent<StatefulInteractable>() != null && transform.GetComponent<StatefulInteractable>().enabled)
            + ",Button=" + (transform.GetComponent<Button>() != null && transform.GetComponent<Button>().enabled);
    }

    private static Transform FindSceneObjectTransform(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool IsSceneObjectActiveSelf(string objectName)
    {
        Transform transform = FindSceneObjectTransform(objectName);
        return transform != null && transform.gameObject.activeSelf;
    }

    private static bool HasComponentIncludingInactive<T>(string objectName) where T : Component
    {
        Transform transform = FindSceneObjectTransform(objectName);
        return transform != null && transform.GetComponent<T>() != null;
    }

    private static bool HasEnabledComponentIncludingInactive<T>(string objectName) where T : Component
    {
        Transform transform = FindSceneObjectTransform(objectName);
        T component = transform != null ? transform.GetComponent<T>() : null;
        if (component == null)
        {
            return false;
        }

        if (component is UnityEngine.Behaviour behaviour)
        {
            return behaviour.enabled;
        }

        if (component is Collider collider)
        {
            return collider.enabled;
        }

        return true;
    }

    private static int CountHandMenuSpawnBottleBridgeCallbacks()
    {
        Transform spawnBottle = FindHandMenuSpawnBottleTransform();
        if (spawnBottle == null)
        {
            return 0;
        }

        int count = 0;
        Button[] buttons = spawnBottle.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            count += CountPersistentMethodCalls(buttons[i].onClick, nameof(PhotonSharedMRHandMenuSpawnBottleBridge.SpawnBottleFromHandMenu));
        }

        StatefulInteractable[] interactables = spawnBottle.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            count += CountPersistentMethodCalls(interactables[i].OnClicked, nameof(PhotonSharedMRHandMenuSpawnBottleBridge.SpawnBottleFromHandMenu));
        }

        return count;
    }

    private static int CountPersistentMethodCalls(UnityEventBase unityEvent, string methodName)
    {
        if (unityEvent == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < unityEvent.GetPersistentEventCount(); i++)
        {
            if (unityEvent.GetPersistentTarget(i) != null
                && unityEvent.GetPersistentMethodName(i) == methodName)
            {
                count++;
            }
        }

        return count;
    }

    private static Transform FindStartupPhotonLoginPanelCanvas()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, "StartupPhotonLoginPanelCanvas");
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static int CountLoginInputNamed(string objectName)
    {
        Transform canvas = FindStartupPhotonLoginPanelCanvas();
        if (canvas == null)
        {
            return 0;
        }

        int count = 0;
        TMP_InputField[] inputs = canvas.GetComponentsInChildren<TMP_InputField>(true);
        for (int i = 0; i < inputs.Length; i++)
        {
            if (inputs[i] != null && inputs[i].name == objectName)
            {
                count++;
            }
        }

        return count;
    }

    private static bool PhotonLoginKeyboardRequired()
    {
        Transform canvas = FindStartupPhotonLoginPanelCanvas();
        return canvas != null && canvas.GetComponentsInChildren<TMP_InputField>(true).Length > 0;
    }

    private static int CountLoginRobotSelectionButtons()
    {
        return CountLoginButtonNamed("AmirButton")
            + CountLoginButtonNamed("RoverButton")
            + CountLoginButtonNamed("DroneButton")
            + CountLoginButtonNamed("ObserverButton");
    }

    private static int CountLoginButtonNamed(string objectName)
    {
        Transform canvas = FindStartupPhotonLoginPanelCanvas();
        if (canvas == null)
        {
            return 0;
        }

        int count = 0;
        Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == objectName)
            {
                count++;
            }
        }

        return count;
    }

    private static string PhotonFixedRoomName()
    {
        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        if (bootstrap != null && !string.IsNullOrWhiteSpace(bootstrap.roomName))
        {
            return bootstrap.roomName;
        }

        return PhotonSharedMRSessionSettings.DefaultRoomName;
    }

    private static bool PhotonUsesAutoHostOrClient()
    {
        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        return bootstrap != null && bootstrap.UsesAutoHostOrClient;
    }

    private static bool PhotonPlayerDisplayNumberNetworked()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        return source.Contains("[Networked] public int DisplayPlayerNumberValue");
    }

    private static bool PhotonAvatarDisplayNameUsesPlayerNumber()
    {
        string path = "Assets/Scripts/PhotonSharedMR/HmdOverheadCursor.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        return source.Contains("DisplayPlayerLabel");
    }

    private static bool PCEditorAutoObserver()
    {
        PhotonSharedMRSessionSettings settings = PhotonSharedMRSessionSettings.CreatePcObserverDefaults(ShareDeviceType.PCEditor);
        return settings.deviceType == ShareDeviceType.PCEditor
            && settings.role == SharedUserRole.Supervisor
            && settings.robotTarget == SharedMRRobotTarget.Observer
            && string.Equals(settings.roomName, PhotonSharedMRSessionSettings.DefaultRoomName, System.StringComparison.Ordinal);
    }

    private static bool PCEditorRobotSelectionUiHidden()
    {
        string path = "Assets/Scripts/PhotonSharedMR/PhotonSharedMRLoginPanel.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        return source.Contains("hideRobotSelectionForPcObserver = true")
            && source.Contains("SetButtonVisible(amirRobotButton, !pcAutoObserver)")
            && source.Contains("SetButtonVisible(roverRobotButton, !pcAutoObserver)")
            && source.Contains("SetButtonVisible(droneRobotButton, !pcAutoObserver)")
            && source.Contains("SetButtonVisible(observerRobotButton, !pcAutoObserver)");
    }

    private static SharedUserRole PCEditorRole()
    {
        return PhotonSharedMRSessionSettings.CreatePcObserverDefaults(ShareDeviceType.PCEditor).role;
    }

    private static SharedMRRobotTarget PCEditorRobotTarget()
    {
        return PhotonSharedMRSessionSettings.CreatePcObserverDefaults(ShareDeviceType.PCEditor).robotTarget;
    }

    private static bool ObserverDisplayNumberNetworked()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        return source.Contains("[Networked] public int ObserverDisplayNumberValue");
    }

    private static string ObserverDisplayNameFormat()
    {
        string sample = PhotonSharedMRSessionSettings.BuildObserverDisplayName(1);
        return sample == "Observer 1" ? "Observer {N}" : sample;
    }

    private static bool ObserverNumberUsesPlayerRef()
    {
        string method = ExtractMethodSource(
            "Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs",
            "AllocateObserverDisplayNumberForStateAuthority");
        if (string.IsNullOrWhiteSpace(method))
        {
            return true;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(method, @"\bPlayerRef\b");
    }

    private static bool ObserverNumbersNotReusedAfterLeave()
    {
        string path = "Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        string leftMethod = ExtractMethodSource(path, "OnPlayerLeft");
        return source.Contains("nextObserverDisplayNumber = observerDisplayNumber + 1")
            && !source.Contains("nextObserverDisplayNumber--")
            && !leftMethod.Contains("nextObserverDisplayNumber");
    }

    private static bool ObserverBottleInteractionAllowed()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("TryBeginLocalGrab")
            && source.Contains("allowStateAuthorityGrab")
            && BottleGrabHasNoRoleRestriction()
            && BottleGrabHasNoRobotTargetRestriction();
    }

    private static bool PCEditorObserverBottleInteractionAllowed()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return ObserverBottleInteractionAllowed()
            && source.Contains("allowMouseEditorGrab")
            && source.Contains("HandlePcBottleMouseInput")
            && source.Contains("Input.GetMouseButtonDown(0)")
            && !source.Contains("PCEditor")
            && !source.Contains("ShareDeviceType");
    }

    private static bool BottleGrabHasNoRoleRestriction()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return !source.Contains("SharedUserRole")
            && !source.Contains("CurrentRole")
            && !source.Contains("Supervisor")
            && !source.Contains("CanControlRobot")
            && !source.Contains("isObserver");
    }

    private static bool BottleGrabHasNoRobotTargetRestriction()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string grabMethod = ExtractMethodSource(path, "TryBeginLocalGrab");
        string canUseMethod = ExtractMethodSource(path, "CanUsePcBottleMouseInput");
        string combined = grabMethod + "\n" + canUseMethod;
        return !combined.Contains("SharedMRRobotTarget")
            && !combined.Contains("RobotTarget")
            && !combined.Contains("DeviceType")
            && !combined.Contains("PCEditor");
    }

    private static bool BottlePosePublishRequiresLockOwnerOnly()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string localDragMethod = ExtractMethodSource(path, "CanPublishLocalDragPose");
        string hostApplyMethod = ExtractMethodSource(path, "HostApplyBottlePose");
        return localDragMethod.Contains("IsGrabbed")
            && localDragMethod.Contains("IsLocalLockOwner()")
            && localDragMethod.Contains("localDragActive")
            && !localDragMethod.Contains("HasStateAuthority")
            && hostApplyMethod.Contains("LockOwner != requestPlayer")
            && hostApplyMethod.Contains("PHOTON_BOTTLE_HOST_POSE_REJECTED");
    }

    private static bool ObserverPositionCursorPrefabAssigned()
    {
        return PrefabHasChildComponent<ObserverPositionCursor>(AvatarPrefabPath);
    }

    private static bool ObserverPositionCursorWhite()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
        ObserverPositionCursor cursor = prefab != null ? prefab.GetComponentInChildren<ObserverPositionCursor>(true) : null;
        return cursor != null && cursor.cursorColor == Color.white;
    }

    private static bool ObserverPositionCursorSynced()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return source.Contains("avatar.HeadWorldPosition")
            && source.Contains("avatar.IsPcObserverAvatar")
            && source.Contains("!avatar.IsLocalUser");
    }

    private static bool ObserverLocalCursorHidden()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return source.Contains("!avatar.IsLocalUser");
    }

    private static bool ObserverRemoteCursorVisible()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return source.Contains("avatar.IsPcObserverAvatar")
            && source.Contains("SetVisible(visible)");
    }

    private static bool NonObserverCursorBehaviorUnchanged()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/HmdOverheadCursor.cs");
        return source.Contains("!avatar.IsPcObserverAvatar")
            && source.Contains("roleFilterVisible")
            && source.Contains("GetRoleColor(role)");
    }

    private static bool PhotonFingerTipNetworkedFieldsPresent()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        string[] required =
        {
            "[Networked] public Vector3 LeftThumbTipPosition",
            "[Networked] public Vector3 LeftIndexTipPosition",
            "[Networked] public Vector3 LeftMiddleTipPosition",
            "[Networked] public Vector3 LeftRingTipPosition",
            "[Networked] public Vector3 LeftLittleTipPosition",
            "[Networked] public Vector3 RightThumbTipPosition",
            "[Networked] public Vector3 RightIndexTipPosition",
            "[Networked] public Vector3 RightMiddleTipPosition",
            "[Networked] public Vector3 RightRingTipPosition",
            "[Networked] public Vector3 RightLittleTipPosition",
            "[Networked] public NetworkBool LeftThumbTipTracked",
            "[Networked] public NetworkBool LeftIndexTipTracked",
            "[Networked] public NetworkBool LeftMiddleTipTracked",
            "[Networked] public NetworkBool LeftRingTipTracked",
            "[Networked] public NetworkBool LeftLittleTipTracked",
            "[Networked] public NetworkBool RightThumbTipTracked",
            "[Networked] public NetworkBool RightIndexTipTracked",
            "[Networked] public NetworkBool RightMiddleTipTracked",
            "[Networked] public NetworkBool RightRingTipTracked",
            "[Networked] public NetworkBool RightLittleTipTracked"
        };

        for (int i = 0; i < required.Length; i++)
        {
            if (!source.Contains(required[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PhotonFingerTipRemoteVisualsPresent()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("EnsureFingerTipVisuals")
            && source.Contains("ApplyFingerTipNetworkStateToVisuals")
            && (source.Contains("SetFingerTipVisible(i, tracked)")
                || source.Contains("SetFingerTipVisible(i, visible)"));
    }

    private static bool PhotonFingerTipLocalVisualsHidden()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("isLocal || IsPcObserverAvatar")
            && source.Contains("SetAllFingerTipVisualsVisible(false)");
    }

    private static bool PhotonFingerTipPcObserverDisabled()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        string sampleMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SampleFingerTipsToBuffer");
        return sampleMethod.Contains("enableFingerTipSync")
            && sampleMethod.Contains("!IsPcObserverAvatar")
            && source.Contains("!IsLocalUser && !IsPcObserverAvatar");
    }

    private static bool PhotonFingerTipNoRuntimeInstantiateLoop()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return !source.Contains("Instantiate(")
            && source.Contains("fingerTipVisuals[i] == null")
            && source.Contains("CreateFingerTipVisual(root, i)");
    }

    private static bool NetworkUserAvatarSpawnGuard()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("private bool networkStateReady")
            && source.Contains("public bool IsNetworkStateReady")
            && source.Contains("private bool CanReadNetworkState")
            && source.Contains("networkStateReady = true;")
            && source.Contains("public override void Despawned(NetworkRunner runner, bool hasState)")
            && source.Contains("private void OnDisable()");
    }

    private static bool HmdOverheadCursorDoesNotReadNetworkedInOnEnable()
    {
        string method = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/HmdOverheadCursor.cs", "OnEnable");
        return !ContainsAvatarNetworkedRead(method)
            && method.Contains("SetVisible(false)");
    }

    private static bool ObserverPositionCursorDoesNotReadNetworkedInOnEnable()
    {
        string awake = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs", "Awake");
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return !ContainsAvatarNetworkedRead(awake)
            && awake.Contains("SetVisible(false)")
            && source.Contains("NotifyAvatarNetworkSpawned()");
    }

    private static bool FingerTipVisualsDoNotReadNetworkedBeforeSpawned()
    {
        string ensureMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "EnsureFingerTipVisuals");
        string applyMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ApplyFingerTipNetworkStateToVisuals");
        return !ensureMethod.Contains("GetFingerTipTracked")
            && !ensureMethod.Contains("GetFingerTipPosition")
            && !ensureMethod.Contains("LeftThumbTipTracked")
            && applyMethod.Contains("if (!IsNetworkStateReady)");
    }

    private static bool AvatarNetworkedPropertiesSafeBeforeSpawned()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("return CanReadNetworkState ? UserNameValue.ToString() : fallbackUserName")
            && source.Contains("return CanReadNetworkState ? ClampRole(RoleValue) : fallbackRole")
            && source.Contains("return CanReadNetworkState ? ClampDeviceType(DeviceTypeValue) : fallbackDeviceType")
            && source.Contains("return CanReadNetworkState ? ClampRobotTarget(RobotTargetValue) : fallbackRobotTarget")
            && source.Contains("return CanReadNetworkState ? DisplayPlayerNumberValue : 0")
            && source.Contains("return CanReadNetworkState ? ObserverDisplayNumberValue : 0")
            && source.Contains("return CanReadNetworkState ? HeadPosition : transform.position")
            && source.Contains("if (!IsNetworkStateReady)")
            && source.Contains("return false;");
    }

    private static int FingerTipCursorObjectsCreated()
    {
        return NetworkUserAvatar.NetworkUserAvatarFingerTipCount;
    }

    private static bool FingerTipCursorRendererAssigned()
    {
        string method = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "CreateFingerTipVisual");
        return method.Contains("Renderer renderer")
            && method.Contains("renderer.sharedMaterial")
            && method.Contains("renderer.enabled = false");
    }

    private static bool FingerTipCursorLayerVisible()
    {
        string method = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "CreateFingerTipVisual");
        return method.Contains("GameObject.CreatePrimitive(PrimitiveType.Sphere)")
            && !method.Contains(".layer =");
    }

    private static string FingerTipCursorScaleMeters()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("fingerTipVisualScale = 0.024f") ? "0.024" : "Unknown";
    }

    private static bool FingerTipReadOnlyAfterSpawned()
    {
        string sampleMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SampleFingerTipsToBuffer");
        string applyMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ApplyFingerTipNetworkStateToVisuals");
        return sampleMethod.Contains("bool canSampleFingerTips = IsLocalAvatarForPoseSource()")
            && sampleMethod.Contains("IsNetworkStateReady")
            && sampleMethod.Contains("enableFingerTipSync")
            && sampleMethod.Contains("!IsPcObserverAvatar")
            && applyMethod.Contains("if (!IsNetworkStateReady)")
            && applyMethod.Contains("GetFingerTipTracked(i)");
    }

    private static bool FingerTipLocalTrackingSourceConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("XRSubsystemHelpers.HandsAggregator")
            && source.Contains("TryGetJoint(FingerTipJoints[index]")
            && source.Contains("TrackedHandJoint.Palm");
    }

    private static bool FingerTipNetworkedWritePathConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("SetFingerTipNetworkState")
            && source.Contains("RPC_SubmitInputAuthorityPose")
            && source.Contains("PHOTON_FINGERTIP_NETWORK_WRITE");
    }

    private static bool FingerTipRemoteApplyPathConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("ApplyFingerTipNetworkStateToVisuals")
            && source.Contains("PHOTON_FINGERTIP_REMOTE_RECEIVED")
            && source.Contains("PHOTON_FINGERTIP_CURSOR_STATE");
    }

    private static bool FingerTipLocalAvatarHidden()
    {
        return PhotonFingerTipLocalVisualsHidden();
    }

    private static bool FingerTipPcObserverHidden()
    {
        return PhotonFingerTipPcObserverDisabled();
    }

    private static bool PcObserverMainCameraPoseSource()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return source.Contains("ShouldUsePcObserverMainCameraPose()")
            && source.Contains("Camera mainCamera = Camera.main")
            && source.Contains("headSource = mainCamera.transform")
            && source.Contains("PHOTON_PC_OBSERVER_CAMERA_MISSING");
    }

    private static bool PcObserverHeadPoseNetworkWriteConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        string sampleMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SampleLocalRigIntoNetworkState");
        string submitMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SubmitInputAuthorityPose");
        return source.Contains("private Transform ResolveHeadPoseSource()")
            && sampleMethod.Contains("Transform resolvedHead = ResolveHeadPoseSource()")
            && sampleMethod.Contains("HeadPosition = resolvedHead.position")
            && sampleMethod.Contains("HeadRotation = resolvedHead.rotation")
            && submitMethod.Contains("Transform resolvedHead = ResolveHeadPoseSource()")
            && submitMethod.Contains("resolvedHead.position")
            && submitMethod.Contains("resolvedHead.rotation");
    }

    private static bool ObserverPositionCursorUsesHeadWorldPosition()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return source.Contains("Vector3 headPosition = avatar.HeadWorldPosition")
            && source.Contains("transform.position = headPosition");
    }

    private static bool ObserverPositionCursorRemoteOnly()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return source.Contains("avatar.IsPcObserverAvatar")
            && source.Contains("!avatar.IsLocalUser")
            && source.Contains("SetVisible(visible)");
    }

    private static bool BottleUsesHostAuthoritativeGrab()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        string hostGrabMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs", "HostHandleBottleGrabRequest");
        return source.Contains("[Rpc(RpcSources.All, RpcTargets.StateAuthority")
            && source.Contains("RPC_RequestBottleGrab")
            && hostGrabMethod.Contains("IsGrabbed = true")
            && hostGrabMethod.Contains("LockOwner = requestPlayer")
            && hostGrabMethod.Contains("PHOTON_BOTTLE_HOST_GRAB_GRANTED")
            && hostGrabMethod.Contains("PHOTON_BOTTLE_HOST_GRAB_REJECTED");
    }

    private static bool BottleClientDoesNotRequireStateAuthority()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string source = ReadSource(path);
        string tryBeginMethod = ExtractSourceBetween(
            source,
            "public bool TryBeginLocalGrab(bool useMouseDrag)",
            "public void EndLocalGrab()");
        string canDragMethod = ExtractMethodSource(path, "CanPublishLocalDragPose");
        string pcGrabMethod = ExtractMethodSource(path, "TryBeginPcBottleMouseGrab");
        string combined = tryBeginMethod + "\n" + canDragMethod + "\n" + pcGrabMethod;
        return combined.Contains("RPC_RequestBottleGrab")
            && combined.Contains("IsLocalLockOwner()")
            && !combined.Contains("HasStateAuthority")
            && !combined.Contains("RequestStateAuthority");
    }

    private static bool BottlePoseWrittenOnlyByHost()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string writeMethod = ExtractMethodSource(path, "WriteNetworkPose");
        string hostApplyMethod = ExtractMethodSource(path, "HostApplyBottlePose");
        string submitMethod = ExtractMethodSource(path, "SubmitLocalBottlePose");
        return writeMethod.Contains("!HasStateAuthority")
            && writeMethod.Contains("NetworkPosition = currentPosition")
            && writeMethod.Contains("NetworkRotation = currentRotation")
            && writeMethod.Contains("NetworkPoseVersion++")
            && hostApplyMethod.Contains("WriteNetworkPose(reason, force)")
            && hostApplyMethod.Contains("LockOwner != requestPlayer")
            && submitMethod.Contains("RPC_SubmitBottlePose")
            && !submitMethod.Contains("NetworkPosition =")
            && !submitMethod.Contains("NetworkRotation =")
            && !submitMethod.Contains("NetworkPoseVersion++");
    }

    private static bool BottleGrabAllowedForObserver()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string tryBeginMethod = ExtractMethodSource(path, "TryBeginLocalGrab");
        string canUseMethod = ExtractMethodSource(path, "CanUsePcBottleMouseInput");
        string hostGrabMethod = ExtractMethodSource(path, "HostHandleBottleGrabRequest");
        string combined = tryBeginMethod + "\n" + canUseMethod + "\n" + hostGrabMethod;
        return !combined.Contains("Supervisor")
            && !combined.Contains("SharedUserRole")
            && !combined.Contains("RobotTarget")
            && !combined.Contains("CanControlRobot")
            && !combined.Contains("IsPcObserverAvatar");
    }

    private static bool PcBottleMouseUsesHostGrabRpc()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string source = ReadSource(path);
        string tryBeginMouse = ExtractMethodSource(path, "TryBeginPcBottleMouseGrab");
        string tryBeginGrab = ExtractSourceBetween(
            source,
            "public bool TryBeginLocalGrab(bool useMouseDrag)",
            "public void EndLocalGrab()");
        return tryBeginMouse.Contains("TryBeginLocalGrab(true)")
            && tryBeginGrab.Contains("RPC_RequestBottleGrab")
            && tryBeginGrab.Contains("Runner.LocalPlayer")
            && !tryBeginGrab.Contains("RequestStateAuthority")
            && !tryBeginMouse.Contains("HasStateAuthority");
    }

    private static bool QuestBottleGrabUsesHostGrabRpc()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string source = ReadSource(path);
        string xrEntered = ExtractMethodSource(path, "OnXrSelectEntered");
        string tryBeginGrab = ExtractSourceBetween(
            source,
            "public bool TryBeginLocalGrab(bool useMouseDrag)",
            "public void EndLocalGrab()");
        return xrEntered.Contains("TryBeginLocalGrab(false)")
            && tryBeginGrab.Contains("RPC_RequestBottleGrab")
            && tryBeginGrab.Contains("Runner.LocalPlayer")
            && !tryBeginGrab.Contains("RequestStateAuthority");
    }

    private static bool PcBottleMouseInteractionEnabled()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("HandlePcBottleMouseInput")
            && source.Contains("TryGetPcBottlePointerHit")
            && source.Contains("Input.GetMouseButtonDown(0)")
            && source.Contains("Plane dragPlane = new Plane(Vector3.up")
            && source.Contains("objectKind == SharedNetworkObjectKind.Bottle")
            && source.Contains("isPhotonSharedNetworkBottle");
    }

    private static bool PcBottleMouseGrabUsesStateAuthority()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("TryBeginLocalGrab(true)")
            && source.Contains("Object.RequestStateAuthority()")
            && source.Contains("PHOTON_PC_BOTTLE_AUTHORITY_WAIT");
    }

    private static bool PcBottleMouseDragUsesLockOwner()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("HasPcMouseDragOwnership()")
            && source.Contains("return IsLocalLockOwner();")
            && source.Contains("PHOTON_PC_BOTTLE_DRAG_STARTED")
            && source.Contains("PHOTON_PC_BOTTLE_DRAGGING")
            && BottlePosePublishRequiresLockOwnerOnly();
    }

    private static bool PcBottleMouseReleaseConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("ReleasePcBottleMouseGrab")
            && source.Contains("Input.GetMouseButtonUp(0)")
            && source.Contains("EndLocalGrab()")
            && source.Contains("PHOTON_PC_BOTTLE_RELEASED");
    }

    private static bool PcBottleInputUpdateConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("private void Update()")
            && source.Contains("HandlePcBottleMouseInput();")
            && source.Contains("PHOTON_PC_BOTTLE_INPUT_STATE")
            && source.Contains("leftMouseDown=")
            && source.Contains("leftMouseHeld=");
    }

    private static bool PcBottleInputDoesNotRequireQuestHandTracking()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return !source.Contains("XRSubsystemHelpers.HandsAggregator")
            && !source.Contains("TrackedHandJoint")
            && !source.Contains("HandJointPose");
    }

    private static bool PcBottleInputDoesNotRequireSupervisorFalse()
    {
        string path = "Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs";
        string canUseMethod = ExtractMethodSource(path, "CanUsePcBottleMouseInput");
        string tryBeginMethod = ExtractMethodSource(path, "TryBeginPcBottleMouseGrab");
        string combined = canUseMethod + "\n" + tryBeginMethod;
        return !combined.Contains("Supervisor")
            && !combined.Contains("SharedUserRole")
            && !combined.Contains("CurrentRole")
            && !combined.Contains("RobotTarget")
            && !combined.Contains("IsPcObserverAvatar");
    }

    private static bool PcBottleRaycastMaskIncludesSharedBottle()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("public LayerMask pcBottleRaycastMask = ~0")
            && source.Contains("Physics.RaycastAll(ray, Mathf.Infinity, pcBottleRaycastMask")
            && source.Contains("IsPcBottleRaycastCandidate()");
    }

    private static bool PcBottleColliderEnabled()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhotonSharedBottlePrefabPath);
        Collider[] colliders = prefab != null ? prefab.GetComponentsInChildren<Collider>(true) : new Collider[0];
        bool prefabHasEnabledCollider = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null && colliders[i].enabled)
            {
                prefabHasEnabledCollider = true;
                break;
            }
        }

        return prefabHasEnabledCollider
            && source.Contains("EnsureSharedBottleCollidersEnabled")
            && source.Contains("targetCollider.enabled = true");
    }

    private static bool PcBottleGrabRequestReachable()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        string tryBeginMouse = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs", "TryBeginPcBottleMouseGrab");
        return tryBeginMouse.Contains("TryGetPcBottlePointerHit")
            && tryBeginMouse.Contains("LogPcBottleState(\"PHOTON_PC_BOTTLE_GRAB_REQUEST\"")
            && tryBeginMouse.Contains("TryBeginLocalGrab(true)")
            && source.Contains("PHOTON_PC_BOTTLE_POINTER_HIT")
            && source.Contains("PHOTON_PC_BOTTLE_POINTER_MISS");
    }

    private static bool PcBottleAuthorityWaitConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("pcBottleAuthorityTimeoutSeconds = 3f")
            && source.Contains("PHOTON_PC_BOTTLE_AUTHORITY_WAIT")
            && source.Contains("PHOTON_PC_BOTTLE_AUTHORITY_GRANTED")
            && source.Contains("PHOTON_PC_BOTTLE_AUTHORITY_TIMEOUT")
            && source.Contains("CheckPcBottleAuthorityTimeout()");
    }

    private static bool PcBottleGrabPermissionWaitConfigured()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkedSharedSceneObject.cs");
        return source.Contains("pcBottleGrabPermissionTimeoutSeconds = 3f")
            && source.Contains("PHOTON_PC_BOTTLE_GRAB_PERMISSION_WAIT")
            && source.Contains("PHOTON_PC_BOTTLE_GRAB_GRANTED")
            && source.Contains("PermissionTimeout")
            && source.Contains("CheckPcBottleGrabPermissionTimeout()");
    }

    private static bool PcBottlePosePublishRequiresStateAuthorityAndLockOwner()
    {
        return false;
    }

    private static bool AvatarPoseSamplingRestrictedToLocalAvatar()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        string fixedUpdate = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "FixedUpdateNetwork");
        string spawned = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "Spawned");
        return source.Contains("private bool IsLocalAvatarForPoseSource()")
            && source.Contains("return Object != null && Object.HasInputAuthority;")
            && fixedUpdate.Contains("if (!IsLocalAvatarForPoseSource())")
            && fixedUpdate.Contains("ResolveSources();")
            && fixedUpdate.Contains("SampleLocalRigIntoNetworkState();")
            && fixedUpdate.Contains("SubmitInputAuthorityPose();")
            && spawned.Contains("bool isLocalAvatar = IsLocalAvatarForPoseSource()")
            && spawned.Contains("if (HasStateAuthority && isLocalAvatar)");
    }

    private static bool RemoteAvatarDoesNotUseCameraMain()
    {
        string resolveSources = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ResolveSources");
        string resolveHead = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ResolveHeadPoseSource");
        string hmdCursor = ReadSource("Assets/Scripts/PhotonSharedMR/HmdOverheadCursor.cs");
        string observerCursor = ReadSource("Assets/Scripts/PhotonSharedMR/ObserverPositionCursor.cs");
        return resolveSources.Contains("if (!IsLocalAvatarForPoseSource())")
            && resolveSources.IndexOf("if (!IsLocalAvatarForPoseSource())", System.StringComparison.Ordinal)
                < resolveSources.IndexOf("Camera.main", System.StringComparison.Ordinal)
            && resolveHead.Contains("if (!IsLocalAvatarForPoseSource())")
            && resolveHead.IndexOf("if (!IsLocalAvatarForPoseSource())", System.StringComparison.Ordinal)
                < resolveHead.IndexOf("Camera.main", System.StringComparison.Ordinal)
            && !hmdCursor.Contains("Camera.main")
            && !observerCursor.Contains("Camera.main")
            && hmdCursor.Contains("NetworkUserAvatar.LocalViewTransform")
            && observerCursor.Contains("NetworkUserAvatar.LocalViewTransform");
    }

    private static bool RemoteAvatarDoesNotUseHandsAggregator()
    {
        string tryGetMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "TryGetFingerTipPosition");
        string sampleMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SampleFingerTipsToBuffer");
        return tryGetMethod.Contains("if (!IsLocalAvatarForPoseSource())")
            && tryGetMethod.IndexOf("if (!IsLocalAvatarForPoseSource())", System.StringComparison.Ordinal)
                < tryGetMethod.IndexOf("XRSubsystemHelpers.HandsAggregator", System.StringComparison.Ordinal)
            && sampleMethod.Contains("bool canSampleFingerTips = IsLocalAvatarForPoseSource()");
    }

    private static bool PcObserverCameraPoseRestrictedToLocalAvatar()
    {
        string resolveSources = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ResolveSources");
        string resolveHead = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ResolveHeadPoseSource");
        return resolveSources.Contains("if (!IsLocalAvatarForPoseSource())")
            && resolveSources.Contains("ShouldUsePcObserverMainCameraPose()")
            && resolveSources.IndexOf("if (!IsLocalAvatarForPoseSource())", System.StringComparison.Ordinal)
                < resolveSources.IndexOf("ShouldUsePcObserverMainCameraPose()", System.StringComparison.Ordinal)
            && resolveHead.Contains("if (!IsLocalAvatarForPoseSource())")
            && resolveHead.Contains("ShouldUsePcObserverMainCameraPose()")
            && resolveHead.IndexOf("if (!IsLocalAvatarForPoseSource())", System.StringComparison.Ordinal)
                < resolveHead.IndexOf("ShouldUsePcObserverMainCameraPose()", System.StringComparison.Ordinal);
    }

    private static bool FingerTipNetworkWriteRestrictedToLocalAvatar()
    {
        string applyBuffer = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ApplyFingerTipBufferToNetworkState");
        string sampleLocalRig = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SampleLocalRigIntoNetworkState");
        string submitPose = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "SubmitInputAuthorityPose");
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        return applyBuffer.Contains("if (!IsLocalAvatarForPoseSource())")
            && sampleLocalRig.Contains("if (!IsLocalAvatarForPoseSource())")
            && submitPose.Contains("if (!IsLocalAvatarForPoseSource())")
            && source.Contains("[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]")
            && source.Contains("PHOTON_FINGERTIP_NETWORK_WRITE");
    }

    private static bool FingerTipRemoteVisualsUseNetworkedValuesOnly()
    {
        string applyMethod = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "ApplyFingerTipNetworkStateToVisuals");
        return applyMethod.Contains("GetFingerTipTracked(i)")
            && applyMethod.Contains("GetFingerTipPosition(i)")
            && applyMethod.Contains("SetPose(fingerTipVisuals[i], remotePosition")
            && !applyMethod.Contains("TryGetFingerTipPosition")
            && !applyMethod.Contains("XRSubsystemHelpers.HandsAggregator")
            && !applyMethod.Contains("Camera.main");
    }

    private static bool EachAvatarHasIndependentPoseSource()
    {
        string source = ReadSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs");
        string fixedUpdate = ExtractMethodSource("Assets/Scripts/PhotonSharedMR/NetworkUserAvatar.cs", "FixedUpdateNetwork");
        return source.Contains("PHOTON_AVATAR_POSE_SOURCE")
            && source.Contains("avatarObjectId=")
            && source.Contains("inputAuthority=")
            && source.Contains("stateAuthority=")
            && source.Contains("isLocalAvatar=")
            && fixedUpdate.Contains("if (!IsLocalAvatarForPoseSource())")
            && !fixedUpdate.Contains("if (!HasStateAuthority)\r\n        {\r\n            return;\r\n        }\r\n\r\n        ResolveSources();");
    }

    private static bool ContainsAvatarNetworkedRead(string source)
    {
        string[] forbidden =
        {
            "IsPcObserverAvatar",
            "DeviceType",
            "CurrentRole",
            "RobotTarget",
            "DisplayPlayerLabel",
            "DisplayPlayerNumber",
            "ObserverDisplayNumber",
            "HeadWorldPosition",
            "LeftThumbTipTracked",
            "RightLittleTipTracked"
        };

        for (int i = 0; i < forbidden.Length; i++)
        {
            if (source.Contains(forbidden[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadSource(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string ExtractSourceBetween(string source, string startNeedle, string endNeedle)
    {
        int start = source.IndexOf(startNeedle, System.StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        int end = source.IndexOf(endNeedle, start + startNeedle.Length, System.StringComparison.Ordinal);
        if (end < 0)
        {
            return source.Substring(start);
        }

        return source.Substring(start, end - start);
    }

    private static string ExtractMethodSource(string path, string methodName)
    {
        if (!File.Exists(path))
        {
            return string.Empty;
        }

        string source = File.ReadAllText(path);
        int start = FindMethodDeclarationStart(source, methodName);
        if (start < 0)
        {
            return string.Empty;
        }

        int bodyStart = source.IndexOf('{', start);
        if (bodyStart < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        for (int i = bodyStart; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source.Substring(start, i - start + 1);
                }
            }
        }

        return source.Substring(start);
    }

    private static int FindMethodDeclarationStart(string source, string methodName)
    {
        int searchStart = 0;
        string needle = methodName + "(";
        while (searchStart < source.Length)
        {
            int candidate = source.IndexOf(needle, searchStart, System.StringComparison.Ordinal);
            if (candidate < 0)
            {
                return -1;
            }

            int lineStart = source.LastIndexOf('\n', candidate);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            string prefix = source.Substring(lineStart, candidate - lineStart).TrimStart();
            if ((prefix.StartsWith("private ", System.StringComparison.Ordinal)
                    || prefix.StartsWith("public ", System.StringComparison.Ordinal)
                    || prefix.StartsWith("internal ", System.StringComparison.Ordinal)
                    || prefix.StartsWith("protected ", System.StringComparison.Ordinal))
                && !prefix.StartsWith("//", System.StringComparison.Ordinal))
            {
                return lineStart;
            }

            searchStart = candidate + needle.Length;
        }

        return -1;
    }

    private static int CountRobotSelectionButtonPhotonJoinRefs()
    {
        return CountLoginButtonPhotonJoinRefs("AmirButton")
            + CountLoginButtonPhotonJoinRefs("RoverButton")
            + CountLoginButtonPhotonJoinRefs("DroneButton")
            + CountLoginButtonPhotonJoinRefs("ObserverButton");
    }

    private static int CountLoginButtonPhotonJoinRefs(string objectName)
    {
        Transform canvas = FindStartupPhotonLoginPanelCanvas();
        if (canvas == null)
        {
            return 0;
        }

        int count = 0;
        Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == objectName)
            {
                count += CountPhotonJoinCallbacks(buttons[i].onClick);
            }
        }

        return count;
    }

    private static int CountJoinButtonStartSessionRefs()
    {
        Transform canvas = FindStartupPhotonLoginPanelCanvas();
        if (canvas == null)
        {
            return 0;
        }

        int count = 0;
        Button[] buttons = canvas.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null && buttons[i].name == "StartButton")
            {
                count += CountPersistentMethodCalls(buttons[i].onClick, nameof(PhotonSharedMRLoginPanel.StartSessionFromUi));
            }
        }

        return count;
    }

    private static bool HandMenuSpawnBottleHasLocalFallback()
    {
        PhotonSharedMRHandMenuSpawnBottleBridge bridge = Object.FindObjectOfType<PhotonSharedMRHandMenuSpawnBottleBridge>(true);
        return bridge != null && bridge.localBottleSpawner != null;
    }

    private static bool HandMenuSpawnBottleHasPhotonSharedBranch()
    {
        PhotonSharedMRHandMenuSpawnBottleBridge bridge = Object.FindObjectOfType<PhotonSharedMRHandMenuSpawnBottleBridge>(true);
        return bridge != null && bridge.sharedBottleSpawner != null;
    }

    private static MoveRobotController FindMoveRobotController()
    {
        return Object.FindObjectOfType<MoveRobotController>(true);
    }

    private static Transform FindHandMenuMenuItemTransform(string itemName)
    {
        Transform menuContent = FindMenuContentTransform();
        return menuContent != null ? FindChildRecursive(menuContent, itemName) : null;
    }

    private static int CountHandMenuMenuItemsNamed(string itemName)
    {
        Transform menuContent = FindMenuContentTransform();
        return menuContent != null ? CountNamedInHierarchy(menuContent, itemName) : 0;
    }

    private static string GetHandMenuMenuItemPath(string itemName)
    {
        Transform item = FindHandMenuMenuItemTransform(itemName);
        return item != null ? GetGameObjectPath(item) : "Missing";
    }

    private static int CountRobotMoveActionOnClicked()
    {
        return CountRobotMoveMethodOnClicked(RobotMoveActionName, nameof(MoveRobotController.PressRobotMoveActionButton));
    }

    private static int CountRobotMoveSettingOnClicked()
    {
        return CountRobotMoveMethodOnClicked(RobotMoveSettingName, nameof(MoveRobotController.PressRobotMoveSettingButton));
    }

    private static int CountRobotMoveMethodOnClicked(string itemName, string methodName)
    {
        Transform item = FindHandMenuMenuItemTransform(itemName);
        if (item == null)
        {
            return 0;
        }

        int count = 0;
        StatefulInteractable[] interactables = item.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            count += CountPersistentMethodCalls(interactables[i].OnClicked, methodName);
        }

        Button[] buttons = item.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            count += CountPersistentMethodCalls(buttons[i].onClick, methodName);
        }

        return count;
    }

    private static int MoveRobotActionListCount()
    {
        MoveRobotController controller = FindMoveRobotController();
        return controller != null ? controller.MoveRobotActionListCount : -1;
    }

    private static int MoveRobotSelectedIndex()
    {
        MoveRobotController controller = FindMoveRobotController();
        return controller != null ? controller.MoveRobotSelectedIndex : -999;
    }

    private static bool MoveRobotIndexValid()
    {
        MoveRobotController controller = FindMoveRobotController();
        return controller != null && controller.MoveRobotIndexValid;
    }

    private static bool MoveRobotRequiredReferences()
    {
        MoveRobotController controller = FindMoveRobotController();
        return controller != null && controller.HasMoveRobotRequiredReferences;
    }

    private static bool PhotonRootOutsideCalibrationHierarchy()
    {
        GameObject root = GameObject.Find("PhotonSharedMR");
        return root != null && !HasCalibrationOrOriginAncestor(root.transform);
    }

    private static bool PhotonBootstrapSceneRoot()
    {
        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        return bootstrap != null && bootstrap.transform.parent == null;
    }

    private static bool PhotonBootstrapOutsideCalibrationHierarchy()
    {
        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        return bootstrap != null && !HasCalibrationOrOriginAncestor(bootstrap.transform);
    }

    private static int PhotonRunnerCount()
    {
#if FUSION_WEAVER && FUSION2
        return CountComponentsInScene<NetworkRunner>();
#else
        return 0;
#endif
    }

    private static bool PhotonScriptsUseEnsureBootstrap()
    {
        string[] scriptPaths =
        {
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRLoginPanel.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonDetectedBottleBridge.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedBottleSpawner.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRButtonCollection.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRHandMenuSpawnBottleBridge.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRDebugPanel.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRMenuPanel.cs"
        };

        for (int i = 0; i < scriptPaths.Length; i++)
        {
            if (!File.Exists(scriptPaths[i]))
            {
                return false;
            }

            string source = File.ReadAllText(scriptPaths[i]);
            if (!source.Contains("EnsureBootstrap(")
                || !source.Contains("PhotonSharedMRBootstrapResolver.EnsureBootstrap"))
            {
                return false;
            }
        }

        return true;
    }

    private static bool PhotonRunnerManagedOnlyByBootstrap()
    {
        return PhotonUnexpectedShutdownCallSites() == 0
            && CountRuntimePhotonScriptOccurrences("AddComponent<NetworkRunner>", "Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs") == 0
            && CountRuntimePhotonScriptOccurrences("new GameObject(\"NetworkRunner", null) == 0;
    }

    private static int PhotonUnexpectedShutdownCallSites()
    {
        return CountRuntimePhotonScriptOccurrences(".Shutdown(", "Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs")
            + CountRuntimePhotonScriptOccurrences("Shutdown()", "Assets/Scripts/PhotonSharedMR/PhotonFusionSharedRoomBootstrap.cs");
    }

    private static int PhotonUnexpectedStartSessionCallSites()
    {
        int count = 0;
        count += CountRuntimePhotonScriptOccurrences(".StartSharedRoom(", "Assets/Scripts/PhotonSharedMR/PhotonSharedMRLoginPanel.cs");
        count += CountRuntimePhotonScriptOccurrences(".StartSessionWithSettings(", null);
        return count;
    }

    private static bool PhotonCalibrationCanShutdownRunner()
    {
        return Directory.Exists("Assets/Scripts/Calibration")
            && (CountFileOccurrencesUnder("Assets/Scripts/Calibration", ".Shutdown(")
                + CountFileOccurrencesUnder("Assets/Scripts/Calibration", "LeaveRoom(")
                + CountFileOccurrencesUnder("Assets/Scripts/Calibration", "StartSharedRoom(")) > 0;
    }

    private static bool PhotonUiCanShutdownRunner()
    {
        string[] uiScripts =
        {
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRLoginPanel.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRButtonCollection.cs",
            "Assets/Scripts/PhotonSharedMR/PhotonSharedMRMenuPanel.cs"
        };

        for (int i = 0; i < uiScripts.Length; i++)
        {
            if (File.Exists(uiScripts[i]) && File.ReadAllText(uiScripts[i]).Contains(".Shutdown("))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PhotonJoinedStateDoesNotRejoin()
    {
        string path = "Assets/Scripts/PhotonSharedMR/PhotonSharedMRLoginPanel.cs";
        if (!File.Exists(path))
        {
            return false;
        }

        string source = File.ReadAllText(path);
        int joinedGuard = source.IndexOf("runnerIsRunning && IsJoinedStatus", System.StringComparison.Ordinal);
        int startSharedRoom = source.IndexOf("await bootstrap.StartSharedRoom", System.StringComparison.Ordinal);
        return joinedGuard >= 0 && startSharedRoom > joinedGuard;
    }

    private static int PhotonLeaveButtonShutdownRefs()
    {
        Transform photonButtons = FindChildRecursiveInScene(PhotonButtonCollectionName);
        Transform leaveButtonTransform = photonButtons != null
            ? FindChildRecursive(photonButtons, LeaveRoomButtonName)
            : null;
        if (leaveButtonTransform == null)
        {
            return 0;
        }

        int count = 0;
        Button[] buttons = leaveButtonTransform.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            count += CountPersistentMethodCalls(buttons[i].onClick, nameof(PhotonSharedMRButtonCollection.LeaveRoom));
        }

        StatefulInteractable[] interactables = leaveButtonTransform.GetComponentsInChildren<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            count += CountPersistentMethodCalls(interactables[i].OnClicked, nameof(PhotonSharedMRButtonCollection.LeaveRoom));
        }

        return count;
    }

    private static int CountRuntimePhotonScriptOccurrences(string pattern, string allowedPath)
    {
        string root = "Assets/Scripts/PhotonSharedMR";
        if (!Directory.Exists(root))
        {
            return 0;
        }

        int count = 0;
        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly);
        for (int i = 0; i < files.Length; i++)
        {
            string normalized = files[i].Replace('\\', '/');
            if (string.Equals(normalized, allowedPath, System.StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/PhotonSharedMRAutoProbe.cs", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            count += CountOccurrences(File.ReadAllText(files[i]), pattern);
        }

        return count;
    }

    private static int CountFileOccurrencesUnder(string root, string pattern)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        int count = 0;
        string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
        for (int i = 0; i < files.Length; i++)
        {
            count += CountOccurrences(File.ReadAllText(files[i]), pattern);
        }

        return count;
    }

    private static bool SourceContains(string path, string pattern)
    {
        return File.Exists(path) && File.ReadAllText(path).Contains(pattern);
    }

    private static int CountCalibrationBlockingCallSites()
    {
        string root = "Assets/Scripts/Calibration";
        string[] patterns =
        {
            "Thread.Sleep",
            "Task.Wait",
            ".Wait(",
            "WaitForCompletion",
            "SceneManager.LoadScene",
            "Resources.UnloadUnusedAssets",
            "GC.Collect"
        };

        int count = 0;
        for (int i = 0; i < patterns.Length; i++)
        {
            count += CountFileOccurrencesUnder(root, patterns[i]);
        }

        return count;
    }

    private static int CountOccurrences(string source, string pattern)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(pattern))
        {
            return 0;
        }

        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(pattern, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static Transform FindChildRecursiveInScene(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform found = FindChildRecursive(roots[i].transform, objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool NetworkRunnerOutsideCalibrationHierarchy()
    {
#if FUSION_WEAVER && FUSION2
        NetworkRunner runner = Object.FindObjectOfType<NetworkRunner>(true);
        Transform target = runner != null ? runner.transform : null;
#else
        PhotonFusionSharedRoomBootstrap bootstrap = Object.FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        Transform target = bootstrap != null ? bootstrap.transform : null;
#endif
        return target != null && !HasCalibrationOrOriginAncestor(target);
    }

    private static bool CalibrationDoesNotReferencePhotonRoot()
    {
        GameObject root = GameObject.Find("PhotonSharedMR");
        if (root == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = Object.FindObjectsOfType<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null || !IsCalibrationComponent(behaviour))
            {
                continue;
            }

            SerializedObject serializedObject;
            try
            {
                serializedObject = new SerializedObject(behaviour);
            }
            catch
            {
                continue;
            }

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (ReferencesPhotonRoot(property.objectReferenceValue, root.transform))
                {
                    Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] Calibration component references PhotonSharedMR"
                        + " component=" + behaviour.GetType().Name
                        + " path=" + GetGameObjectPath(behaviour.transform)
                        + " property=" + property.propertyPath);
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasCalibrationOrOriginAncestor(Transform target)
    {
        Transform current = target != null ? target.parent : null;
        while (current != null)
        {
            string normalized = current.name.Replace(" ", string.Empty).ToLowerInvariant();
            if (normalized.Contains("calibration")
                || normalized == "origin"
                || normalized == "boundingbox"
                || normalized == "absolutecalibrationsphereroot")
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static bool IsCalibrationComponent(MonoBehaviour behaviour)
    {
        string typeName = behaviour.GetType().Name;
        return typeName == nameof(SelectObject)
            || typeName == nameof(ObjectCalobration)
            || typeName == nameof(CalibrationController)
            || typeName.IndexOf("Calibration", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ReferencesPhotonRoot(Object reference, Transform photonRoot)
    {
        if (reference == null || photonRoot == null)
        {
            return false;
        }

        Transform referenceTransform = null;
        if (reference is GameObject gameObject)
        {
            referenceTransform = gameObject.transform;
        }
        else if (reference is Component component)
        {
            referenceTransform = component.transform;
        }

        return referenceTransform != null
            && (referenceTransform == photonRoot || referenceTransform.IsChildOf(photonRoot));
    }

    private static bool MoveRobotUpdateYouBotTransformConfigured()
    {
        UpdateYouBotTransform updateTransform = MoveRobotUpdateYouBotTransform();
        if (updateTransform == null)
        {
            return false;
        }

        SerializedObject updateObject = new SerializedObject(updateTransform);
        SerializedProperty subscriberProperty = updateObject.FindProperty("youBotPosSubscriber");
        SerializedProperty youbotProperty = updateObject.FindProperty("YoubotObject");
        return subscriberProperty != null
            && subscriberProperty.objectReferenceValue != null
            && youbotProperty != null
            && youbotProperty.objectReferenceValue != null;
    }

    private static string MoveRobotUpdateYouBotTransformPath()
    {
        UpdateYouBotTransform updateTransform = MoveRobotUpdateYouBotTransform();
        return updateTransform != null ? GetGameObjectPath(updateTransform.transform) : "Missing";
    }

    private static UpdateYouBotTransform MoveRobotUpdateYouBotTransform()
    {
        MoveRobotController controller = FindMoveRobotController();
        if (controller == null)
        {
            return null;
        }

        SerializedObject controllerObject = new SerializedObject(controller);
        SerializedProperty updateProperty = controllerObject.FindProperty("updateYouBotTransform");
        return updateProperty != null ? updateProperty.objectReferenceValue as UpdateYouBotTransform : null;
    }

    private static bool PhotonDetectedBottleBridgeHasSubscriber()
    {
        PhotonDetectedBottleBridge bridge = Object.FindObjectOfType<PhotonDetectedBottleBridge>(true);
        return bridge != null && bridge.detectedBottleSubscriber != null;
    }

    private static bool PhotonDetectedBottleBridgeHasSpawner()
    {
        PhotonDetectedBottleBridge bridge = Object.FindObjectOfType<PhotonDetectedBottleBridge>(true);
        return bridge != null && bridge.sharedBottleSpawner != null;
    }

    private static bool PhotonDetectedBottleBridgeHasBootstrap()
    {
        PhotonDetectedBottleBridge bridge = Object.FindObjectOfType<PhotonDetectedBottleBridge>(true);
        return bridge != null && bridge.bootstrap != null;
    }

    private static string PhotonDetectedBottleBridgeAuthorityMode()
    {
        PhotonDetectedBottleBridge bridge = Object.FindObjectOfType<PhotonDetectedBottleBridge>(true);
        return bridge != null ? bridge.authorityMode.ToString() : "Missing";
    }

    private static string DetectedBottlePoseSubscriberTopic()
    {
        DetectedBottlePoseSubscriber subscriber = Object.FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        return subscriber != null ? subscriber.Topic : "Missing";
    }

    private static string DetectedBottlePoseSubscriberPoseArrayTopic()
    {
        DetectedBottlePoseSubscriber subscriber = Object.FindObjectOfType<DetectedBottlePoseSubscriber>(true);
        return subscriber != null ? subscriber.PoseArrayTopic : "Missing";
    }

    private static bool IsPhotonSharedBottlePrefabRegistered()
    {
#if FUSION_WEAVER && FUSION2
        return NetworkProjectConfigUtilities.TryGetPrefabId(PhotonSharedBottlePrefabPath, out NetworkPrefabId _);
#else
        return false;
#endif
    }

    private static bool PhotonSharedBottlePrefabHasNetworkTransform()
    {
#if FUSION_WEAVER && FUSION2
        return PrefabHasComponent<NetworkTransform>(PhotonSharedBottlePrefabPath);
#else
        return false;
#endif
    }

    private static string GetMovedHandMenuItemPaths()
    {
        return "CalibrationSelect=" + GetDirectMenuContentChildPathNamed("CalibrationSelect")
            + " CalibrationOperation=" + GetDirectMenuContentChildPathNamed("CalibrationOperation")
            + " RobotMoveSetting=" + GetDirectMenuContentChildPathNamed("Robot Move setting")
            + " RobotMoveAction=" + GetDirectMenuContentChildPathNamed("Robot Move Action")
            + " GripperSet=" + GetDirectMenuContentChildPathNamed("Gripper Set")
            + " OriginSet=" + GetDirectMenuContentChildPathsStartingWith("Origin Set")
            + " Stop=" + GetDirectMenuContentChildPathNamed("Stop")
            + " AddView=" + GetDirectMenuContentChildPathNamed("AddView")
            + " IRM=" + GetDirectMenuContentChildPathNamed("IRM")
            + " guras=" + GetDirectMenuContentChildPathNamed("guras")
            + " SpawnBottle=" + GetDirectMenuContentChildPathNamed("Spawn Bottle");
    }

    private static string GetDirectMenuContentChildPathNamed(string objectName)
    {
        Transform menuContent = FindPhotonSettingMenuContentParent();
        if (menuContent == null)
        {
            return "MissingMenuContent";
        }

        for (int i = 0; i < menuContent.childCount; i++)
        {
            Transform child = menuContent.GetChild(i);
            if (child.name == objectName)
            {
                return GetGameObjectPath(child);
            }
        }

        return "Missing";
    }

    private static string GetDirectMenuContentChildPathsStartingWith(string objectNamePrefix)
    {
        Transform menuContent = FindPhotonSettingMenuContentParent();
        if (menuContent == null)
        {
            return "MissingMenuContent";
        }

        string result = string.Empty;
        for (int i = 0; i < menuContent.childCount; i++)
        {
            Transform child = menuContent.GetChild(i);
            if (child.name.Trim().StartsWith(objectNamePrefix))
            {
                if (!string.IsNullOrEmpty(result))
                {
                    result += "|";
                }

                result += GetGameObjectPath(child);
            }
        }

        return string.IsNullOrEmpty(result) ? "Missing" : result;
    }

    private static int CountMissingScriptsInScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        int count = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            Transform[] transforms = roots[i].GetComponentsInChildren<Transform>(true);
            for (int j = 0; j < transforms.Length; j++)
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transforms[j].gameObject);
            }
        }

        return count;
    }

    private static int CountMissingButtonOnClickTargets()
    {
        int count = 0;
        Button[] buttons = Object.FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            Button button = buttons[i];
            if (button == null || button.onClick == null)
            {
                continue;
            }

            for (int eventIndex = 0; eventIndex < button.onClick.GetPersistentEventCount(); eventIndex++)
            {
                if (button.onClick.GetPersistentTarget(eventIndex) == null
                    && !string.IsNullOrEmpty(button.onClick.GetPersistentMethodName(eventIndex)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static int CountMissingStatefulInteractableOnClickedTargets()
    {
        int count = 0;
        StatefulInteractable[] interactables = Object.FindObjectsOfType<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length; i++)
        {
            StatefulInteractable interactable = interactables[i];
            if (interactable == null || interactable.OnClicked == null)
            {
                continue;
            }

            for (int eventIndex = 0; eventIndex < interactable.OnClicked.GetPersistentEventCount(); eventIndex++)
            {
                if (interactable.OnClicked.GetPersistentTarget(eventIndex) == null
                    && !string.IsNullOrEmpty(interactable.OnClicked.GetPersistentMethodName(eventIndex)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static string GetMissingUnityEventTargetDetails(int maxItems)
    {
        string result = string.Empty;
        int emitted = 0;

        Button[] buttons = Object.FindObjectsOfType<Button>(true);
        for (int i = 0; i < buttons.Length && emitted < maxItems; i++)
        {
            Button button = buttons[i];
            if (button == null || button.onClick == null)
            {
                continue;
            }

            for (int eventIndex = 0; eventIndex < button.onClick.GetPersistentEventCount() && emitted < maxItems; eventIndex++)
            {
                if (button.onClick.GetPersistentTarget(eventIndex) == null
                    && !string.IsNullOrEmpty(button.onClick.GetPersistentMethodName(eventIndex)))
                {
                    if (!string.IsNullOrEmpty(result))
                    {
                        result += " | ";
                    }

                    result += "Button:" + GetGameObjectPath(button.transform)
                        + "." + button.onClick.GetPersistentMethodName(eventIndex);
                    emitted++;
                }
            }
        }

        StatefulInteractable[] interactables = Object.FindObjectsOfType<StatefulInteractable>(true);
        for (int i = 0; i < interactables.Length && emitted < maxItems; i++)
        {
            StatefulInteractable interactable = interactables[i];
            if (interactable == null || interactable.OnClicked == null)
            {
                continue;
            }

            for (int eventIndex = 0; eventIndex < interactable.OnClicked.GetPersistentEventCount() && emitted < maxItems; eventIndex++)
            {
                if (interactable.OnClicked.GetPersistentTarget(eventIndex) == null
                    && !string.IsNullOrEmpty(interactable.OnClicked.GetPersistentMethodName(eventIndex)))
                {
                    if (!string.IsNullOrEmpty(result))
                    {
                        result += " | ";
                    }

                    result += "StatefulInteractable:" + GetGameObjectPath(interactable.transform)
                        + "." + interactable.OnClicked.GetPersistentMethodName(eventIndex);
                    emitted++;
                }
            }
        }

        return string.IsNullOrEmpty(result) ? "None" : result;
    }

    private static bool SharedObjectPoseSyncEnabled(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        NetworkedSharedSceneObject sharedObject = found != null ? found.GetComponent<NetworkedSharedSceneObject>() : null;
        return sharedObject != null && sharedObject.syncPose;
    }

    private static bool PrefabHasComponent<T>(string prefabPath) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        return prefab != null && prefab.GetComponent<T>() != null;
    }

    private static bool PrefabHasChildComponent<T>(string prefabPath) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        return prefab != null && prefab.GetComponentInChildren<T>(true) != null;
    }

    private static int PrefabComponentCount<T>(string prefabPath) where T : Component
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        return prefab != null ? prefab.GetComponentsInChildren<T>(true).Length : 0;
    }

    private static int PrefabRendererCount(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        return prefab != null ? prefab.GetComponentsInChildren<Renderer>(true).Length : 0;
    }

    private static int CountSceneObjectsNamed(string objectName)
    {
        Scene scene = SceneManager.GetActiveScene();
        int count = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            count += CountNamedInHierarchy(roots[i].transform, objectName);
        }

        return count;
    }

    private static int CountHmdFrontSpawnUiObjects()
    {
        return CountSceneObjectsNamed("SharedBottleSpawnerCanvas")
            + CountSceneObjectsNamed("PhotonSharedMRHmdFrontSpawnUI")
            + CountSceneObjectsNamed("HmdFrontSpawnUI");
    }

    private static int CountNamedInHierarchy(Transform transform, string objectName)
    {
        int count = transform.name == objectName ? 1 : 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            count += CountNamedInHierarchy(transform.GetChild(i), objectName);
        }

        return count;
    }

    private static int CountComponentsInScene<T>() where T : Component
    {
        Scene scene = SceneManager.GetActiveScene();
        int count = 0;
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            count += roots[i].GetComponentsInChildren<T>(true).Length;
        }

        return count;
    }

    private static Material EnsureNetworkBottleMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(NetworkBottleMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader);
            AssetDatabase.CreateAsset(material, NetworkBottleMaterialPath);
        }

        material.color = new Color(0.1f, 0.9f, 0.75f, 1f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureCylinderVisual(GameObject target, Material material)
    {
        MeshFilter filter = target.GetComponent<MeshFilter>();
        MeshRenderer renderer = target.GetComponent<MeshRenderer>();
        if (filter == null || renderer == null || filter.sharedMesh == null)
        {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            MeshFilter sourceFilter = primitive.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = target.AddComponent<MeshFilter>();
            }

            if (renderer == null)
            {
                renderer = target.AddComponent<MeshRenderer>();
            }

            filter.sharedMesh = sourceFilter.sharedMesh;
            Object.DestroyImmediate(primitive);
        }

        renderer.sharedMaterial = material;
    }

    private static bool EnsureOptionalComponent(GameObject target, string logName, params string[] typeNames)
    {
        System.Type componentType = FindComponentType(typeNames);
        if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
        {
            return false;
        }

        Component component = target.GetComponent(componentType);
        if (component == null)
        {
            component = target.AddComponent(componentType);
            Debug.Log("[SharePhotonSharedMRSceneConfigurator] Added " + logName + " to Photon shared bottle prefab");
        }

        if (component is UnityEngine.Behaviour behaviour)
        {
            behaviour.enabled = true;
        }

        return true;
    }

    private static System.Type FindComponentType(params string[] typeNames)
    {
        for (int i = 0; i < typeNames.Length; i++)
        {
            System.Type directType = System.Type.GetType(typeNames[i]);
            if (directType != null)
            {
                return directType;
            }
        }

        System.Reflection.Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
        for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
        {
            for (int typeIndex = 0; typeIndex < typeNames.Length; typeIndex++)
            {
                System.Type type = assemblies[assemblyIndex].GetType(typeNames[typeIndex]);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0)
        {
            return existing;
        }

        Object[] tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] TagManager.asset was not found; using Default layer.");
            return 0;
        }

        SerializedObject tagManager = new SerializedObject(tagManagerAssets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");
        if (layers == null)
        {
            Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] TagManager layers property was not found; using Default layer.");
            return 0;
        }

        for (int i = 8; i < layers.arraySize; i++)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(layer.stringValue))
            {
                layer.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                return i;
            }
        }

        Debug.LogWarning("[SharePhotonSharedMRSceneConfigurator] No empty user layer slot for " + layerName + "; using Default layer.");
        return 0;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        for (int i = 0; i < target.transform.childCount; i++)
        {
            SetLayerRecursively(target.transform.GetChild(i).gameObject, layer);
        }
    }

    private static string PrefabRootLayerName(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            return "MissingPrefab";
        }

        string layerName = LayerMask.LayerToName(prefab.layer);
        return string.IsNullOrEmpty(layerName) ? prefab.layer.ToString() : layerName;
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }
}
