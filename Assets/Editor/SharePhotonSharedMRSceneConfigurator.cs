using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#if FUSION_WEAVER && FUSION2
using Fusion;
using Fusion.Editor;
#endif

public static class SharePhotonSharedMRSceneConfigurator
{
    private const string MainScenePath = "Assets/Scenes/main.unity";
    private const string PrefabFolder = "Assets/Prefabs/PhotonSharedMR";
    private const string AvatarPrefabPath = PrefabFolder + "/NetworkUserAvatar.prefab";

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

    public static void ConfigureMainSceneThreeTimesAndVerify()
    {
        for (int i = 0; i < 3; i++)
        {
            ConfigureMainScene();
        }

        VerifyMainScene();
    }

    public static void ConfigureMainScene()
    {
        Scene scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        Directory.CreateDirectory(PrefabFolder);

        GameObject avatarPrefab = EnsureAvatarPrefab();

        GameObject root = EnsureSceneObject("PhotonSharedMR");
        GameObject bootstrapObject = EnsureChild(root, "PhotonSharedRoomBootstrap");
        PhotonFusionSharedRoomBootstrap bootstrap = EnsureComponent<PhotonFusionSharedRoomBootstrap>(bootstrapObject);
        bootstrap.roomName = "SHARE-MR-Room";
        bootstrap.autoJoinOnStart = false;
        bootstrap.maxPlayers = 8;
        bootstrap.initialRole = SharedUserRole.ManipulatorOperator;
        bootstrap.defaultSessionSettings = PhotonSharedMRSessionSettings.CreateDefault();
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

        GameObject virtualAmir = EnsureChild(root, "NetworkedVirtualAmir");
        virtualAmir.transform.localPosition = new Vector3(0f, 0.45f, 1.05f);
        ConfigureSharedObject(virtualAmir, SharedNetworkObjectKind.VirtualRobot);
        VirtualAmirStateDisplay display = EnsureComponent<VirtualAmirStateDisplay>(virtualAmir);
        NetworkedVirtualAmirState networkedState = EnsureComponent<NetworkedVirtualAmirState>(virtualAmir);
        networkedState.display = display;

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
        filter.alwaysVisibleObjects = new[] { sharedObjectsRoot, virtualAmir };
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

        GameObject loginObject = EnsureChild(root, "LoginPanel");
        PhotonSharedMRLoginPanel loginPanel = EnsureComponent<PhotonSharedMRLoginPanel>(loginObject);
        loginPanel.bootstrap = bootstrap;
        loginPanel.roleFilter = filter;
        loginPanel.defaultSettings = PhotonSharedMRSessionSettings.CreateDefault();
        loginPanel.suppressBootstrapAutoJoin = true;
        loginPanel.hidePanelAfterStart = true;
        loginPanel.spawnDistance = 0.85f;
        loginPanel.verticalOffset = -0.08f;
        loginPanel.panelScale = 0.0015f;

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
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY loginPanel="
            + HasComponent<PhotonSharedMRLoginPanel>("LoginPanel"));

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_COUNTS"
            + " PhotonSharedMR=" + CountSceneObjectsNamed("PhotonSharedMR")
            + " LoginPanel=" + CountSceneObjectsNamed("LoginPanel")
            + " PhotonSharedRoomBootstrap=" + CountSceneObjectsNamed("PhotonSharedRoomBootstrap")
            + " HmdOverheadCursorScene=" + CountComponentsInScene<HmdOverheadCursor>()
            + " HmdOverheadCursorPrefab=" + PrefabComponentCount<HmdOverheadCursor>(AvatarPrefabPath));

#if FUSION_WEAVER && FUSION2
        NetworkProjectConfigUtilities.RebuildPrefabTable();
        AssetDatabase.SaveAssets();

        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY networkRunner="
            + HasComponent<NetworkRunner>("PhotonSharedRoomBootstrap"));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY_COUNTS_FUSION"
            + " NetworkRunner=" + CountComponentsInScene<NetworkRunner>()
            + " NetworkObjectScene=" + CountComponentsInScene<NetworkObject>()
            + " NetworkObjectAvatarPrefab=" + PrefabComponentCount<NetworkObject>(AvatarPrefabPath));
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY avatarPrefab NetworkObject="
            + PrefabHasComponent<NetworkObject>(AvatarPrefabPath)
            + " NetworkUserAvatar=" + PrefabHasComponent<NetworkUserAvatar>(AvatarPrefabPath)
            + " HmdOverheadCursor=" + PrefabHasChildComponent<HmdOverheadCursor>(AvatarPrefabPath)
            + " registered=" + NetworkProjectConfigUtilities.TryGetPrefabId(AvatarPrefabPath, out NetworkPrefabId avatarPrefabId)
            + " prefabId=" + avatarPrefabId);
        Debug.Log("[SharePhotonSharedMRSceneConfigurator] VERIFY NetworkObject bottle="
            + HasComponent<NetworkObject>("NetworkedBottleProxy")
            + " box=" + HasComponent<NetworkObject>("NetworkedBoxProxy")
            + " obstacle=" + HasComponent<NetworkObject>("NetworkedObstacleProxy")
            + " virtualAmir=" + HasComponent<NetworkObject>("NetworkedVirtualAmir"));
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
#if FUSION_WEAVER && FUSION2
        EnsureComponent<NetworkObject>(avatar);
#endif
        prefab = PrefabUtility.SaveAsPrefabAsset(avatar, AvatarPrefabPath);
        Object.DestroyImmediate(avatar);
        return prefab;
    }

    private static void ConfigureSharedObject(GameObject target, SharedNetworkObjectKind kind)
    {
        NetworkedSharedSceneObject sharedObject = EnsureComponent<NetworkedSharedSceneObject>(target);
        sharedObject.objectKind = kind;
        sharedObject.allowStateAuthorityGrab = true;
        sharedObject.allowMouseEditorGrab = kind == SharedNetworkObjectKind.Bottle || kind == SharedNetworkObjectKind.Box;
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

    private static Transform FindTransform(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private static bool HasComponent<T>(string objectName) where T : Component
    {
        GameObject found = GameObject.Find(objectName);
        return found != null && found.GetComponent<T>() != null;
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
