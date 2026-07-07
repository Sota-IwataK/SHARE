using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using UnityEngine;
using UnityEngine.XR;

#if FUSION_WEAVER && FUSION2
using Fusion;
#endif

[DisallowMultipleComponent]
public class NetworkUserAvatar :
#if FUSION_WEAVER && FUSION2
    NetworkBehaviour
#else
    MonoBehaviour
#endif
{
    public const int NetworkUserAvatarFingerTipCount = 10;

    public static NetworkUserAvatar Local { get; private set; }
    public static Transform LocalViewTransform { get; private set; }
    private static PhotonSharedMRSessionSettings pendingLocalSessionSettings;

    private static readonly string[] FingerTipNames =
    {
        "LeftThumbTip",
        "LeftIndexTip",
        "LeftMiddleTip",
        "LeftRingTip",
        "LeftLittleTip",
        "RightThumbTip",
        "RightIndexTip",
        "RightMiddleTip",
        "RightRingTip",
        "RightLittleTip"
    };

    private static readonly TrackedHandJoint[] FingerTipJoints =
    {
        TrackedHandJoint.ThumbTip,
        TrackedHandJoint.IndexTip,
        TrackedHandJoint.MiddleTip,
        TrackedHandJoint.RingTip,
        TrackedHandJoint.LittleTip,
        TrackedHandJoint.ThumbTip,
        TrackedHandJoint.IndexTip,
        TrackedHandJoint.MiddleTip,
        TrackedHandJoint.RingTip,
        TrackedHandJoint.LittleTip
    };

    private static readonly XRNode[] FingerTipHands =
    {
        XRNode.LeftHand,
        XRNode.LeftHand,
        XRNode.LeftHand,
        XRNode.LeftHand,
        XRNode.LeftHand,
        XRNode.RightHand,
        XRNode.RightHand,
        XRNode.RightHand,
        XRNode.RightHand,
        XRNode.RightHand
    };

    [Header("User Metadata")]
    [SerializeField] private string fallbackUserName = PhotonSharedMRSessionSettings.DefaultUserName;
    [SerializeField] private SharedUserRole fallbackRole = SharedUserRole.ManipulatorOperator;
    [SerializeField] private ShareDeviceType fallbackDeviceType = ShareDeviceType.PCEditor;
    [SerializeField] private SharedMRRobotTarget fallbackRobotTarget = SharedMRRobotTarget.Amir;
    [SerializeField] private bool fallbackIsHostLikeUser = true;

    [Header("Local Tracking Sources")]
    [SerializeField] private bool autoResolveSources = true;
    [SerializeField] private Transform headSource;
    [SerializeField] private Transform leftHandSource;
    [SerializeField] private Transform rightHandSource;

    [Header("Avatar Visuals")]
    [SerializeField] private Transform headVisual;
    [SerializeField] private Transform leftHandVisual;
    [SerializeField] private Transform rightHandVisual;
    [SerializeField] private ObserverPositionCursor observerPositionCursor;
    [SerializeField] private bool hideLocalVisuals = false;

    [Header("Finger Tip Sync")]
    [SerializeField] private bool enableFingerTipSync = true;
    [SerializeField] private bool enableFingerTipDebugLogs;
    [SerializeField] private float fingerTipVisualScale = 0.024f;
    [SerializeField] private Color leftFingerTipColor = new Color(0.2f, 0.9f, 0.45f, 0.65f);
    [SerializeField] private Color rightFingerTipColor = new Color(1f, 0.6f, 0.15f, 0.65f);

    [Header("Debug")]
    [SerializeField] private bool enableAvatarDebugLogs;

    private readonly Vector3[] sampledFingerTipPositions = new Vector3[NetworkUserAvatarFingerTipCount];
    private readonly bool[] sampledFingerTipTracked = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastLoggedLocalFingerTipSourceFound = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastLoggedLocalFingerTipTracked = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] localFingerTipSourceLogged = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastLoggedNetworkFingerTipTracked = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] networkFingerTipWriteLogged = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastReceivedRemoteFingerTipTracked = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] remoteFingerTipReceiveLogged = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastAppliedRemoteFingerTipTracked = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] lastFingerTipRendererVisible = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] fingerTipCursorStateLogged = new bool[NetworkUserAvatarFingerTipCount];
    private readonly bool[] warnedMissingFingerTipWhileHandTracked = new bool[NetworkUserAvatarFingerTipCount];
    private Transform[] fingerTipVisuals;
    private bool fingerTipSyncReadyLogged;
    private bool rightIndexVisualInitializedLogged;
    private bool networkStateReady;
    private bool pcObserverCameraMissingLogged;
    private bool avatarPoseSourceLogged;
    private bool lastAvatarPoseSourceIsLocalAvatar;
    private string lastAvatarPoseSourceCameraName;
    private float lastAvatarPoseSourceLogTime = -999f;

#if FUSION_WEAVER && FUSION2
    [Networked] public NetworkString<_64> UserNameValue { get; set; }
    [Networked] public int RoleValue { get; set; }
    [Networked] public int DeviceTypeValue { get; set; }
    [Networked] public int RobotTargetValue { get; set; }
    [Networked] public int DisplayPlayerNumberValue { get; set; }
    [Networked] public int ObserverDisplayNumberValue { get; set; }
    [Networked] public NetworkBool IsHostLikeUserValue { get; set; }
    [Networked] public Vector3 HeadPosition { get; set; }
    [Networked] public Quaternion HeadRotation { get; set; }
    [Networked] public Vector3 LeftHandPosition { get; set; }
    [Networked] public Quaternion LeftHandRotation { get; set; }
    [Networked] public Vector3 RightHandPosition { get; set; }
    [Networked] public Quaternion RightHandRotation { get; set; }
    [Networked] public Vector3 LeftThumbTipPosition { get; set; }
    [Networked] public Vector3 LeftIndexTipPosition { get; set; }
    [Networked] public Vector3 LeftMiddleTipPosition { get; set; }
    [Networked] public Vector3 LeftRingTipPosition { get; set; }
    [Networked] public Vector3 LeftLittleTipPosition { get; set; }
    [Networked] public Vector3 RightThumbTipPosition { get; set; }
    [Networked] public Vector3 RightIndexTipPosition { get; set; }
    [Networked] public Vector3 RightMiddleTipPosition { get; set; }
    [Networked] public Vector3 RightRingTipPosition { get; set; }
    [Networked] public Vector3 RightLittleTipPosition { get; set; }
    [Networked] public NetworkBool LeftThumbTipTracked { get; set; }
    [Networked] public NetworkBool LeftIndexTipTracked { get; set; }
    [Networked] public NetworkBool LeftMiddleTipTracked { get; set; }
    [Networked] public NetworkBool LeftRingTipTracked { get; set; }
    [Networked] public NetworkBool LeftLittleTipTracked { get; set; }
    [Networked] public NetworkBool RightThumbTipTracked { get; set; }
    [Networked] public NetworkBool RightIndexTipTracked { get; set; }
    [Networked] public NetworkBool RightMiddleTipTracked { get; set; }
    [Networked] public NetworkBool RightRingTipTracked { get; set; }
    [Networked] public NetworkBool RightLittleTipTracked { get; set; }
#endif

    public bool IsNetworkStateReady
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return networkStateReady;
#else
            return true;
#endif
        }
    }

    private bool CanReadNetworkState
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return networkStateReady && Object != null;
#else
            return true;
#endif
        }
    }

    public string CurrentUserName
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? UserNameValue.ToString() : fallbackUserName;
#else
            return fallbackUserName;
#endif
        }
    }

    public SharedUserRole CurrentRole
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? ClampRole(RoleValue) : fallbackRole;
#else
            return fallbackRole;
#endif
        }
    }

    public ShareDeviceType DeviceType
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? ClampDeviceType(DeviceTypeValue) : fallbackDeviceType;
#else
            return fallbackDeviceType;
#endif
        }
    }

    public SharedMRRobotTarget RobotTarget
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? ClampRobotTarget(RobotTargetValue) : fallbackRobotTarget;
#else
            return fallbackRobotTarget;
#endif
        }
    }

    public bool IsHostLikeUser
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? IsHostLikeUserValue : fallbackIsHostLikeUser;
#else
            return fallbackIsHostLikeUser;
#endif
        }
    }

    public bool IsPcObserverAvatar
    {
        get
        {
            if (!IsNetworkStateReady)
            {
                return false;
            }

            return PhotonSharedMRSessionSettings.IsPcObserverDevice(DeviceType)
                && RobotTarget == SharedMRRobotTarget.Observer
                && CurrentRole == SharedUserRole.Supervisor;
        }
    }

    public bool IsLocalUser
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return Object != null && Object.HasInputAuthority;
#else
            return Local == this;
#endif
        }
    }

    private bool IsLocalAvatarForPoseSource()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null && Object.HasInputAuthority;
#else
        return Local == this;
#endif
    }

    public int DisplayPlayerNumber
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? DisplayPlayerNumberValue : 0;
#else
            return 0;
#endif
        }
    }

    public int ObserverDisplayNumber
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? ObserverDisplayNumberValue : 0;
#else
            return 0;
#endif
        }
    }

    public string DisplayPlayerLabel
    {
        get
        {
            int observerNumber = ObserverDisplayNumber;
            if (observerNumber > 0)
            {
                return PhotonSharedMRSessionSettings.BuildObserverDisplayName(observerNumber);
            }

            string userName = CurrentUserName;
            if (!string.IsNullOrWhiteSpace(userName)
                && !string.Equals(userName, PhotonSharedMRSessionSettings.DefaultUserName, System.StringComparison.Ordinal)
                && !string.Equals(userName, "Player", System.StringComparison.Ordinal)
                && !userName.StartsWith("Player ", System.StringComparison.Ordinal))
            {
                return userName;
            }

            return PhotonSharedMRSessionSettings.BuildRobotDisplayName(RobotTarget, CurrentRole);
        }
    }

    public Transform HeadVisual => headVisual;

    public Vector3 HeadWorldPosition
    {
        get
        {
            if (headVisual != null)
            {
                return headVisual.position;
            }

#if FUSION_WEAVER && FUSION2
            return CanReadNetworkState ? HeadPosition : transform.position;
#else
            return transform.position;
#endif
        }
    }

    public void ConfigureLocalSources(Transform head, Transform leftHand, Transform rightHand)
    {
        headSource = head;
        leftHandSource = leftHand;
        rightHandSource = rightHand;
    }

    public void SetRole(SharedUserRole role)
    {
        fallbackRole = role;
#if FUSION_WEAVER && FUSION2
        if (Object != null && HasStateAuthority)
        {
            RoleValue = (int)role;
        }
#endif
    }

    public void ApplyLocalSessionSettings(PhotonSharedMRSessionSettings settings)
    {
        settings ??= PhotonSharedMRSessionSettings.CreateDefault();
        settings.Sanitize();

        fallbackUserName = settings.userName;
        fallbackRole = settings.role;
        fallbackDeviceType = settings.deviceType;
        fallbackRobotTarget = settings.robotTarget;
        fallbackIsHostLikeUser = settings.isHostLikeUser;

#if FUSION_WEAVER && FUSION2
        if (Object != null && HasStateAuthority)
        {
            UserNameValue = settings.userName;
            RoleValue = (int)settings.role;
            DeviceTypeValue = (int)settings.deviceType;
            RobotTargetValue = (int)settings.robotTarget;
            IsHostLikeUserValue = settings.isHostLikeUser;
            EnsureObserverDisplayNumberAssigned();
            ApplyResolvedDisplayNameToNetworkState();
        }
        else if (Object != null && Object.HasInputAuthority)
        {
            RPC_ApplyInputAuthorityMetadata(
                (int)settings.role,
                (int)settings.deviceType,
                (int)settings.robotTarget,
                settings.isHostLikeUser);
        }
#endif
    }

    public static void SetPendingLocalSessionSettings(PhotonSharedMRSessionSettings settings)
    {
        pendingLocalSessionSettings = settings != null
            ? settings.Clone()
            : PhotonSharedMRSessionSettings.CreateDefault();
        pendingLocalSessionSettings.Sanitize();
    }

    public void SetDisplayPlayerNumber(int displayPlayerNumber)
    {
        int clampedNumber = Mathf.Max(1, displayPlayerNumber);
#if FUSION_WEAVER && FUSION2
        if (Object != null && HasStateAuthority)
        {
            DisplayPlayerNumberValue = clampedNumber;
            ApplyResolvedDisplayNameToNetworkState();
        }
#endif
    }

#if FUSION_WEAVER && FUSION2
    public void EnsureObserverDisplayNumberAssigned()
    {
        if (Object == null || !HasStateAuthority)
        {
            return;
        }

        if (!IsPcObserverMetadata())
        {
            ObserverDisplayNumberValue = 0;
            return;
        }

        if (CanReadNetworkState && ObserverDisplayNumberValue > 0)
        {
            ApplyResolvedDisplayNameToNetworkState();
            return;
        }

        PhotonFusionSharedRoomBootstrap bootstrap = FindObjectOfType<PhotonFusionSharedRoomBootstrap>(true);
        int observerDisplayNumber = bootstrap != null
            ? bootstrap.AllocateObserverDisplayNumberForStateAuthority()
            : Mathf.Max(1, CanReadNetworkState ? DisplayPlayerNumberValue : 1);
        ObserverDisplayNumberValue = Mathf.Max(1, observerDisplayNumber);
        ApplyResolvedDisplayNameToNetworkState();
        Debug.Log("[NetworkUserAvatar] OBSERVER_DISPLAY_NUMBER_ASSIGNED"
            + " observerDisplayNumber=" + observerDisplayNumber
            + " displayName=" + PhotonSharedMRSessionSettings.BuildObserverDisplayName(observerDisplayNumber)
            + " inputAuthority=" + (Object != null ? Object.InputAuthority.ToString() : "None")
            + " stateAuthority=" + (Object != null ? Object.StateAuthority.ToString() : "None"));
    }
#endif

#if FUSION_WEAVER && FUSION2
    private void Awake()
    {
        networkStateReady = false;
        EnsureVisuals();
        LogAvatarDebug("PHOTON_AVATAR_AWAKE networkStateReady=false");
    }

    public override void Spawned()
    {
        networkStateReady = true;
        EnsureVisuals();
        bool isLocalAvatar = IsLocalAvatarForPoseSource();
        if (isLocalAvatar)
        {
            Local = this;
            ResolveSources();
            if (pendingLocalSessionSettings != null)
            {
                ApplyLocalSessionSettings(pendingLocalSessionSettings);
            }
        }

        if (HasStateAuthority && isLocalAvatar)
        {
            ApplyLocalFallbackMetadataToNetworkState();
            SampleLocalRigIntoNetworkState();
        }

        SetLocalVisualVisibility();
        NotifyAvatarNetworkSpawned();
        LogAvatarDebug("PHOTON_AVATAR_SPAWNED networkStateReady=true");
        Debug.Log("[NetworkUserAvatar] Spawned"
            + " PlayerRef=" + (Object != null ? Object.InputAuthority.ToString() : "None")
            + " IsLocalAvatar=" + IsLocalUser
            + " Object.InputAuthority=" + (Object != null ? Object.InputAuthority.ToString() : "None")
            + " Object.StateAuthority=" + (Object != null ? Object.StateAuthority.ToString() : "None")
            + " transform.position=" + transform.position
            + " activeInHierarchy=" + gameObject.activeInHierarchy
            + " rendererEnabledCount=" + CountEnabledRenderers());
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        networkStateReady = false;
        NotifyAvatarNetworkDespawned();
        SetAllFingerTipVisualsVisible(false);
        if (Local == this)
        {
            Local = null;
            LocalViewTransform = null;
        }
    }

    public override void FixedUpdateNetwork()
    {
        if (!IsLocalAvatarForPoseSource())
        {
            return;
        }

        ResolveSources();
        if (HasStateAuthority)
        {
            SampleLocalRigIntoNetworkState();
        }
        else
        {
            SubmitInputAuthorityPose();
        }
    }

    public override void Render()
    {
        EnsureVisuals();
        if (!IsNetworkStateReady)
        {
            SetAllFingerTipVisualsVisible(false);
            return;
        }

        ApplyNetworkStateToVisuals();
        SetLocalVisualVisibility();
    }
#else
    private void Awake()
    {
        networkStateReady = false;
        EnsureVisuals();
    }

    private void OnEnable()
    {
        if (Local == null)
        {
            Local = this;
        }
    }

    private void Update()
    {
        ResolveSources();
        ApplyFallbackPose();
        SetLocalVisualVisibility();
    }
#endif

    private void OnDisable()
    {
        networkStateReady = false;
        NotifyAvatarNetworkDespawned();
        SetAllFingerTipVisualsVisible(false);
        if (Local == this)
        {
            Local = null;
            LocalViewTransform = null;
        }
    }

    private void NotifyAvatarNetworkSpawned()
    {
        HmdOverheadCursor[] overheadCursors = GetComponentsInChildren<HmdOverheadCursor>(true);
        for (int i = 0; i < overheadCursors.Length; i++)
        {
            if (overheadCursors[i] != null)
            {
                overheadCursors[i].NotifyAvatarNetworkSpawned();
            }
        }

        if (observerPositionCursor != null)
        {
            observerPositionCursor.NotifyAvatarNetworkSpawned();
        }
    }

    private void NotifyAvatarNetworkDespawned()
    {
        HmdOverheadCursor[] overheadCursors = GetComponentsInChildren<HmdOverheadCursor>(true);
        for (int i = 0; i < overheadCursors.Length; i++)
        {
            if (overheadCursors[i] != null)
            {
                overheadCursors[i].NotifyAvatarNetworkDespawned();
            }
        }

        if (observerPositionCursor != null)
        {
            observerPositionCursor.NotifyAvatarNetworkDespawned();
        }
    }

    private void EnsureVisuals()
    {
        if (headVisual == null)
        {
            headVisual = CreatePrimitiveVisual("Head", PrimitiveType.Sphere, new Vector3(0.14f, 0.14f, 0.14f), new Color(0.1f, 0.55f, 1f, 0.85f));
        }

        if (leftHandVisual == null)
        {
            leftHandVisual = CreatePrimitiveVisual("LeftHand", PrimitiveType.Sphere, new Vector3(0.07f, 0.07f, 0.07f), new Color(0.2f, 0.9f, 0.45f, 0.85f));
        }

        if (rightHandVisual == null)
        {
            rightHandVisual = CreatePrimitiveVisual("RightHand", PrimitiveType.Sphere, new Vector3(0.07f, 0.07f, 0.07f), new Color(1f, 0.6f, 0.15f, 0.85f));
        }

        EnsureObserverPositionCursor();
        EnsureFingerTipVisuals();
    }

    private void EnsureObserverPositionCursor()
    {
        if (observerPositionCursor == null)
        {
            observerPositionCursor = GetComponentInChildren<ObserverPositionCursor>(true);
        }

        if (observerPositionCursor == null)
        {
            GameObject cursorObject = new GameObject("ObserverPositionCursor");
            cursorObject.transform.SetParent(transform, false);
            observerPositionCursor = cursorObject.AddComponent<ObserverPositionCursor>();
        }

        observerPositionCursor.avatar = this;
        if (observerPositionCursor.headAnchor == null)
        {
            observerPositionCursor.headAnchor = headVisual;
        }
    }

    private void EnsureFingerTipVisuals()
    {
        if (fingerTipVisuals == null || fingerTipVisuals.Length != NetworkUserAvatarFingerTipCount)
        {
            fingerTipVisuals = new Transform[NetworkUserAvatarFingerTipCount];
        }

        Transform root = transform.Find("FingerTipCursors");
        if (root == null)
        {
            GameObject rootObject = new GameObject("FingerTipCursors");
            rootObject.transform.SetParent(transform, false);
            root = rootObject.transform;
        }

        for (int i = 0; i < NetworkUserAvatarFingerTipCount; i++)
        {
            if (fingerTipVisuals[i] == null)
            {
                Transform existing = root.Find(FingerTipNames[i]);
                if (existing != null)
                {
                    fingerTipVisuals[i] = existing;
                }
            }

            if (fingerTipVisuals[i] == null)
            {
                fingerTipVisuals[i] = CreateFingerTipVisual(root, i);
            }

            fingerTipVisuals[i].localScale = GetLocalScaleForWorldSize(
                fingerTipVisuals[i].parent,
                Mathf.Max(0.001f, fingerTipVisualScale));
            LogRightIndexVisualInitializedOnce(fingerTipVisuals[i], i);
        }

        if (!fingerTipSyncReadyLogged)
        {
            fingerTipSyncReadyLogged = true;
            LogFingerTipDebug(
                "PHOTON_FINGERTIP_SYNC_READY",
                "All",
                "All",
                false,
                "networkReady=" + IsNetworkStateReady);
        }
    }

    private Transform CreateFingerTipVisual(Transform root, int index)
    {
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.name = FingerTipNames[index];
        visual.transform.SetParent(root, false);
        visual.transform.localScale = GetLocalScaleForWorldSize(root, Mathf.Max(0.001f, fingerTipVisualScale));

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            Destroy(visualCollider);
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateMaterial(index < 5 ? leftFingerTipColor : rightFingerTipColor);
            renderer.enabled = false;
        }

        return visual.transform;
    }

    private void LogRightIndexVisualInitializedOnce(Transform visual, int index)
    {
        if (rightIndexVisualInitializedLogged || index != 6 || visual == null)
        {
            return;
        }

        rightIndexVisualInitializedLogged = true;
        Debug.Log("[PhotonHandVisual] Right index visual initialized:"
            + "\nobject=" + visual.name
            + "\npath=" + GetHierarchyPath(visual)
            + "\nlocalScale=" + visual.localScale.ToString("F4")
            + "\nlossyScale=" + visual.lossyScale.ToString("F4")
            + "\nisLocal=" + IsLocalUser);
    }

    private static Vector3 GetLocalScaleForWorldSize(Transform parent, float worldSize)
    {
        if (parent == null)
        {
            return Vector3.one * worldSize;
        }

        Vector3 parentScale = parent.lossyScale;
        return new Vector3(
            worldSize * SafeInverseScale(parentScale.x),
            worldSize * SafeInverseScale(parentScale.y),
            worldSize * SafeInverseScale(parentScale.z));
    }

    private static float SafeInverseScale(float value)
    {
        return Mathf.Abs(value) > 0.0001f ? 1f / value : 1f;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "null";
        }

        string path = target.name;
        Transform current = target.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }

    private Transform CreatePrimitiveVisual(string visualName, PrimitiveType primitiveType, Vector3 localScale, Color color)
    {
        GameObject visual = GameObject.CreatePrimitive(primitiveType);
        visual.name = visualName;
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = localScale;

        Collider visualCollider = visual.GetComponent<Collider>();
        if (visualCollider != null)
        {
            Destroy(visualCollider);
        }

        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateMaterial(color);
        }

        return visual.transform;
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

    private void ResolveSources()
    {
        if (!autoResolveSources)
        {
            return;
        }

        if (!IsLocalAvatarForPoseSource())
        {
            return;
        }

        if (ShouldUsePcObserverMainCameraPose())
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headSource = mainCamera.transform;
            }
            else
            {
                LogPcObserverCameraMissingOnce();
            }
        }
        else if (headSource == null && Camera.main != null)
        {
            headSource = Camera.main.transform;
        }

        if (leftHandSource == null)
        {
            leftHandSource = FindSceneTransform("LeftHand") ?? FindSceneTransform("XRHand_Palm");
        }

        if (rightHandSource == null)
        {
            rightHandSource = FindSceneTransform("RightHand");
        }

        if (headSource != null)
        {
            LocalViewTransform = headSource;
        }
    }

    private static Transform FindSceneTransform(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

#if FUSION_WEAVER && FUSION2
    private void ApplyLocalFallbackMetadataToNetworkState()
    {
        UserNameValue = string.IsNullOrWhiteSpace(fallbackUserName)
            ? PhotonSharedMRSessionSettings.DefaultUserName
            : fallbackUserName;
        RoleValue = (int)fallbackRole;
        DeviceTypeValue = (int)fallbackDeviceType;
        RobotTargetValue = (int)fallbackRobotTarget;
        IsHostLikeUserValue = fallbackIsHostLikeUser;
        EnsureObserverDisplayNumberAssigned();
        ApplyResolvedDisplayNameToNetworkState();
    }

    private void SampleLocalRigIntoNetworkState()
    {
        if (!IsLocalAvatarForPoseSource())
        {
            return;
        }

        Transform resolvedHead = ResolveHeadPoseSource();
        HeadPosition = resolvedHead.position;
        HeadRotation = resolvedHead.rotation;

        Vector3 headRight = resolvedHead.right;
        Vector3 headForward = resolvedHead.forward;
        Vector3 leftFallback = resolvedHead.position - headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;
        Vector3 rightFallback = resolvedHead.position + headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;

        LeftHandPosition = leftHandSource != null ? leftHandSource.position : leftFallback;
        LeftHandRotation = leftHandSource != null ? leftHandSource.rotation : resolvedHead.rotation;
        RightHandPosition = rightHandSource != null ? rightHandSource.position : rightFallback;
        RightHandRotation = rightHandSource != null ? rightHandSource.rotation : resolvedHead.rotation;
        SampleFingerTipsToBuffer();
        ApplyFingerTipBufferToNetworkState();
    }

    private void SubmitInputAuthorityPose()
    {
        if (!IsLocalAvatarForPoseSource())
        {
            return;
        }

        Transform resolvedHead = ResolveHeadPoseSource();
        Vector3 headRight = resolvedHead.right;
        Vector3 headForward = resolvedHead.forward;
        Vector3 leftFallback = resolvedHead.position - headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;
        Vector3 rightFallback = resolvedHead.position + headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;
        SampleFingerTipsToBuffer();

        RPC_SubmitInputAuthorityPose(
            resolvedHead.position,
            resolvedHead.rotation,
            leftHandSource != null ? leftHandSource.position : leftFallback,
            leftHandSource != null ? leftHandSource.rotation : resolvedHead.rotation,
            rightHandSource != null ? rightHandSource.position : rightFallback,
            rightHandSource != null ? rightHandSource.rotation : resolvedHead.rotation,
            sampledFingerTipPositions[0],
            sampledFingerTipPositions[1],
            sampledFingerTipPositions[2],
            sampledFingerTipPositions[3],
            sampledFingerTipPositions[4],
            sampledFingerTipPositions[5],
            sampledFingerTipPositions[6],
            sampledFingerTipPositions[7],
            sampledFingerTipPositions[8],
            sampledFingerTipPositions[9],
            sampledFingerTipTracked[0],
            sampledFingerTipTracked[1],
            sampledFingerTipTracked[2],
            sampledFingerTipTracked[3],
            sampledFingerTipTracked[4],
            sampledFingerTipTracked[5],
            sampledFingerTipTracked[6],
            sampledFingerTipTracked[7],
            sampledFingerTipTracked[8],
            sampledFingerTipTracked[9]);
    }

    private Transform ResolveHeadPoseSource()
    {
        if (!IsLocalAvatarForPoseSource())
        {
            return transform;
        }

        Transform resolvedHead = headSource != null ? headSource : transform;
        if (ShouldUsePcObserverMainCameraPose())
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                headSource = mainCamera.transform;
                resolvedHead = mainCamera.transform;
                LocalViewTransform = resolvedHead;
                LogAvatarPoseSource(resolvedHead);
                return resolvedHead;
            }

            LogPcObserverCameraMissingOnce();
        }

        LocalViewTransform = resolvedHead;
        LogAvatarPoseSource(resolvedHead);
        return resolvedHead;
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_ApplyInputAuthorityMetadata(int roleValue, int deviceTypeValue, int robotTargetValue, bool isHostLikeUserValue)
    {
        RoleValue = roleValue;
        DeviceTypeValue = deviceTypeValue;
        RobotTargetValue = robotTargetValue;
        IsHostLikeUserValue = isHostLikeUserValue;
        EnsureObserverDisplayNumberAssigned();
        ApplyResolvedDisplayNameToNetworkState();
    }

    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void RPC_SubmitInputAuthorityPose(
        Vector3 headPosition,
        Quaternion headRotation,
        Vector3 leftHandPosition,
        Quaternion leftHandRotation,
        Vector3 rightHandPosition,
        Quaternion rightHandRotation,
        Vector3 leftThumbTipPosition,
        Vector3 leftIndexTipPosition,
        Vector3 leftMiddleTipPosition,
        Vector3 leftRingTipPosition,
        Vector3 leftLittleTipPosition,
        Vector3 rightThumbTipPosition,
        Vector3 rightIndexTipPosition,
        Vector3 rightMiddleTipPosition,
        Vector3 rightRingTipPosition,
        Vector3 rightLittleTipPosition,
        bool leftThumbTipTracked,
        bool leftIndexTipTracked,
        bool leftMiddleTipTracked,
        bool leftRingTipTracked,
        bool leftLittleTipTracked,
        bool rightThumbTipTracked,
        bool rightIndexTipTracked,
        bool rightMiddleTipTracked,
        bool rightRingTipTracked,
        bool rightLittleTipTracked)
    {
        HeadPosition = headPosition;
        HeadRotation = headRotation;
        LeftHandPosition = leftHandPosition;
        LeftHandRotation = leftHandRotation;
        RightHandPosition = rightHandPosition;
        RightHandRotation = rightHandRotation;
        SetFingerTipNetworkState(0, leftThumbTipPosition, leftThumbTipTracked);
        SetFingerTipNetworkState(1, leftIndexTipPosition, leftIndexTipTracked);
        SetFingerTipNetworkState(2, leftMiddleTipPosition, leftMiddleTipTracked);
        SetFingerTipNetworkState(3, leftRingTipPosition, leftRingTipTracked);
        SetFingerTipNetworkState(4, leftLittleTipPosition, leftLittleTipTracked);
        SetFingerTipNetworkState(5, rightThumbTipPosition, rightThumbTipTracked);
        SetFingerTipNetworkState(6, rightIndexTipPosition, rightIndexTipTracked);
        SetFingerTipNetworkState(7, rightMiddleTipPosition, rightMiddleTipTracked);
        SetFingerTipNetworkState(8, rightRingTipPosition, rightRingTipTracked);
        SetFingerTipNetworkState(9, rightLittleTipPosition, rightLittleTipTracked);
    }

    private void ApplyNetworkStateToVisuals()
    {
        if (!IsNetworkStateReady)
        {
            return;
        }

        SetPose(headVisual, HeadPosition, HeadRotation);
        SetPose(leftHandVisual, LeftHandPosition, LeftHandRotation);
        SetPose(rightHandVisual, RightHandPosition, RightHandRotation);
        ApplyFingerTipNetworkStateToVisuals();
    }

    private void SampleFingerTipsToBuffer()
    {
        bool canSampleFingerTips = IsLocalAvatarForPoseSource()
            && IsNetworkStateReady
            && enableFingerTipSync
            && !IsPcObserverAvatar;
        for (int i = 0; i < NetworkUserAvatarFingerTipCount; i++)
        {
            Vector3 position = Vector3.zero;
            bool sourceFound = false;
            bool handTracked = false;
            bool tracked = canSampleFingerTips && TryGetFingerTipPosition(i, out position, out sourceFound, out handTracked);
            sampledFingerTipTracked[i] = tracked;
            sampledFingerTipPositions[i] = tracked ? position : Vector3.zero;
            LogFingerTipLocalSourceIfChanged(i, sourceFound, handTracked, tracked, sampledFingerTipPositions[i]);
            if (enableFingerTipDebugLogs
                && canSampleFingerTips
                && handTracked
                && !tracked
                && !warnedMissingFingerTipWhileHandTracked[i])
            {
                warnedMissingFingerTipWhileHandTracked[i] = true;
                Debug.LogWarning("[NetworkUserAvatar] PHOTON_FINGERTIP_LOCAL_SOURCE_MISSING_TIP"
                    + " hand=" + GetFingerTipHandName(i)
                    + " finger=" + FingerTipNames[i]
                    + " tracked=False"
                    + " networkReady=" + IsNetworkStateReady);
            }
        }
    }

    private void ApplyFingerTipBufferToNetworkState()
    {
        if (!IsLocalAvatarForPoseSource())
        {
            return;
        }

        for (int i = 0; i < NetworkUserAvatarFingerTipCount; i++)
        {
            SetFingerTipNetworkState(i, sampledFingerTipPositions[i], sampledFingerTipTracked[i]);
        }
    }

    private void ApplyFingerTipNetworkStateToVisuals()
    {
        EnsureFingerTipVisuals();
        if (!IsNetworkStateReady)
        {
            SetAllFingerTipVisualsVisible(false);
            return;
        }

        bool canShowRemoteFingerTips = enableFingerTipSync && !IsLocalUser && !IsPcObserverAvatar;
        for (int i = 0; i < NetworkUserAvatarFingerTipCount; i++)
        {
            bool remoteTracked = GetFingerTipTracked(i);
            Vector3 remotePosition = remoteTracked ? GetFingerTipPosition(i) : Vector3.zero;
            if (!IsLocalUser)
            {
                LogFingerTipRemoteReceivedIfChanged(i, remoteTracked, remotePosition);
            }

            bool visible = canShowRemoteFingerTips && remoteTracked;
            if (visible)
            {
                SetPose(fingerTipVisuals[i], remotePosition, Quaternion.identity);
            }

            SetFingerTipVisible(i, visible);
            if (!IsLocalUser && lastAppliedRemoteFingerTipTracked[i] != visible)
            {
                lastAppliedRemoteFingerTipTracked[i] = visible;
                LogFingerTipDebug(
                    "PHOTON_FINGERTIP_REMOTE_APPLIED",
                    GetFingerTipHandName(i),
                    FingerTipNames[i],
                    visible,
                    "position=" + FormatVector(remotePosition)
                    + " networkReady=" + IsNetworkStateReady);
            }
        }
    }

    private bool TryGetFingerTipPosition(int index, out Vector3 position, out bool sourceFound, out bool handTracked)
    {
        position = Vector3.zero;
        sourceFound = false;
        handTracked = false;
        if (!IsLocalAvatarForPoseSource())
        {
            return false;
        }

        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            return false;
        }

        sourceFound = true;
        handTracked = aggregator.TryGetJoint(TrackedHandJoint.Palm, FingerTipHands[index], out _);
        if (!aggregator.TryGetJoint(FingerTipJoints[index], FingerTipHands[index], out HandJointPose pose))
        {
            return false;
        }

        position = pose.Position;
        return true;
    }

    private void SetFingerTipNetworkState(int index, Vector3 position, bool tracked)
    {
        switch (index)
        {
            case 0:
                LeftThumbTipPosition = position;
                LeftThumbTipTracked = tracked;
                break;
            case 1:
                LeftIndexTipPosition = position;
                LeftIndexTipTracked = tracked;
                break;
            case 2:
                LeftMiddleTipPosition = position;
                LeftMiddleTipTracked = tracked;
                break;
            case 3:
                LeftRingTipPosition = position;
                LeftRingTipTracked = tracked;
                break;
            case 4:
                LeftLittleTipPosition = position;
                LeftLittleTipTracked = tracked;
                break;
            case 5:
                RightThumbTipPosition = position;
                RightThumbTipTracked = tracked;
                break;
            case 6:
                RightIndexTipPosition = position;
                RightIndexTipTracked = tracked;
                break;
            case 7:
                RightMiddleTipPosition = position;
                RightMiddleTipTracked = tracked;
                break;
            case 8:
                RightRingTipPosition = position;
                RightRingTipTracked = tracked;
                break;
            case 9:
                RightLittleTipPosition = position;
                RightLittleTipTracked = tracked;
                break;
            default:
                return;
        }

        LogFingerTipNetworkWriteIfChanged(index, tracked, position);
    }

    private Vector3 GetFingerTipPosition(int index)
    {
        switch (index)
        {
            case 0:
                return LeftThumbTipPosition;
            case 1:
                return LeftIndexTipPosition;
            case 2:
                return LeftMiddleTipPosition;
            case 3:
                return LeftRingTipPosition;
            case 4:
                return LeftLittleTipPosition;
            case 5:
                return RightThumbTipPosition;
            case 6:
                return RightIndexTipPosition;
            case 7:
                return RightMiddleTipPosition;
            case 8:
                return RightRingTipPosition;
            case 9:
                return RightLittleTipPosition;
            default:
                return Vector3.zero;
        }
    }

    private bool GetFingerTipTracked(int index)
    {
        switch (index)
        {
            case 0:
                return LeftThumbTipTracked;
            case 1:
                return LeftIndexTipTracked;
            case 2:
                return LeftMiddleTipTracked;
            case 3:
                return LeftRingTipTracked;
            case 4:
                return LeftLittleTipTracked;
            case 5:
                return RightThumbTipTracked;
            case 6:
                return RightIndexTipTracked;
            case 7:
                return RightMiddleTipTracked;
            case 8:
                return RightRingTipTracked;
            case 9:
                return RightLittleTipTracked;
            default:
                return false;
        }
    }
#else
    private void ApplyFallbackPose()
    {
        Transform resolvedHead = headSource != null ? headSource : transform;
        Vector3 headRight = resolvedHead.right;
        Vector3 headForward = resolvedHead.forward;
        Vector3 leftFallback = resolvedHead.position - headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;
        Vector3 rightFallback = resolvedHead.position + headRight * 0.22f + headForward * 0.35f - Vector3.up * 0.25f;

        SetPose(headVisual, resolvedHead.position, resolvedHead.rotation);
        SetPose(leftHandVisual, leftHandSource != null ? leftHandSource.position : leftFallback, leftHandSource != null ? leftHandSource.rotation : resolvedHead.rotation);
        SetPose(rightHandVisual, rightHandSource != null ? rightHandSource.position : rightFallback, rightHandSource != null ? rightHandSource.rotation : resolvedHead.rotation);
        SetAllFingerTipVisualsVisible(false);
    }
#endif

    private static void SetPose(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target == null)
        {
            return;
        }

        target.SetPositionAndRotation(position, rotation);
    }

    private void SetLocalVisualVisibility()
    {
#if FUSION_WEAVER && FUSION2
        bool isLocal = IsLocalUser;
#else
        bool isLocal = Local == this;
#endif
        bool visible = !(isLocal && hideLocalVisuals);
        SetRendererVisibility(headVisual, visible);
        SetRendererVisibility(leftHandVisual, visible);
        SetRendererVisibility(rightHandVisual, visible);
        if (isLocal || IsPcObserverAvatar)
        {
            SetAllFingerTipVisualsVisible(false);
        }
    }

    private void SetAllFingerTipVisualsVisible(bool visible)
    {
        if (fingerTipVisuals == null)
        {
            return;
        }

        for (int i = 0; i < fingerTipVisuals.Length; i++)
        {
            SetFingerTipVisible(i, visible);
        }
    }

    private void SetFingerTipVisible(int index, bool visible)
    {
        if (fingerTipVisuals == null
            || index < 0
            || index >= fingerTipVisuals.Length
            || fingerTipVisuals[index] == null)
        {
            return;
        }

        if (lastFingerTipRendererVisible[index] == visible && fingerTipCursorStateLogged[index])
        {
            return;
        }

        lastFingerTipRendererVisible[index] = visible;
        SetRendererVisibility(fingerTipVisuals[index], visible);
        LogFingerTipCursorState(index, visible);
    }

    private static void SetRendererVisibility(Transform target, bool visible)
    {
        if (target == null)
        {
            return;
        }

        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = visible;
        }
    }

    private int CountEnabledRenderers()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        int count = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].enabled)
            {
                count++;
            }
        }

        return count;
    }

    private void LogFingerTipLocalSourceIfChanged(int index, bool sourceFound, bool handTracked, bool tracked, Vector3 position)
    {
        if (!enableFingerTipDebugLogs)
        {
            return;
        }

        if (localFingerTipSourceLogged[index]
            && lastLoggedLocalFingerTipSourceFound[index] == sourceFound
            && lastLoggedLocalFingerTipTracked[index] == tracked)
        {
            return;
        }

        localFingerTipSourceLogged[index] = true;
        lastLoggedLocalFingerTipSourceFound[index] = sourceFound;
        lastLoggedLocalFingerTipTracked[index] = tracked;
        LogFingerTipDebug(
            "PHOTON_FINGERTIP_LOCAL_SOURCE",
            GetFingerTipHandName(index),
            FingerTipNames[index],
            tracked,
            "transformFound=" + sourceFound
            + " handTracked=" + handTracked
            + " position=" + FormatVector(position)
            + " networkReady=" + IsNetworkStateReady);
        LogFingerTipDebug(
            "PHOTON_FINGERTIP_TRACKING_CHANGED",
            GetFingerTipHandName(index),
            FingerTipNames[index],
            tracked,
            "position=" + FormatVector(position)
            + " networkReady=" + IsNetworkStateReady);
    }

    private void LogFingerTipNetworkWriteIfChanged(int index, bool tracked, Vector3 position)
    {
        if (!enableFingerTipDebugLogs)
        {
            return;
        }

        if (networkFingerTipWriteLogged[index] && lastLoggedNetworkFingerTipTracked[index] == tracked)
        {
            return;
        }

        networkFingerTipWriteLogged[index] = true;
        lastLoggedNetworkFingerTipTracked[index] = tracked;
        LogFingerTipDebug(
            "PHOTON_FINGERTIP_NETWORK_WRITE",
            GetFingerTipHandName(index),
            FingerTipNames[index],
            tracked,
            "position=" + FormatVector(position)
            + " hasStateAuthority=" + HasStateAuthorityForDebug());
    }

    private void LogFingerTipRemoteReceivedIfChanged(int index, bool tracked, Vector3 position)
    {
        if (!enableFingerTipDebugLogs)
        {
            return;
        }

        if (remoteFingerTipReceiveLogged[index] && lastReceivedRemoteFingerTipTracked[index] == tracked)
        {
            return;
        }

        remoteFingerTipReceiveLogged[index] = true;
        lastReceivedRemoteFingerTipTracked[index] = tracked;
        LogFingerTipDebug(
            "PHOTON_FINGERTIP_REMOTE_RECEIVED",
            GetFingerTipHandName(index),
            FingerTipNames[index],
            tracked,
            "position=" + FormatVector(position)
            + " networkReady=" + IsNetworkStateReady);
    }

    private void LogFingerTipCursorState(int index, bool cursorActive)
    {
        if (!enableFingerTipDebugLogs)
        {
            return;
        }

        Transform cursor = fingerTipVisuals != null && index >= 0 && index < fingerTipVisuals.Length
            ? fingerTipVisuals[index]
            : null;
        Renderer renderer = cursor != null ? cursor.GetComponentInChildren<Renderer>(true) : null;
        bool rendererEnabled = renderer != null && renderer.enabled;
        fingerTipCursorStateLogged[index] = true;
        LogFingerTipDebug(
            "PHOTON_FINGERTIP_CURSOR_STATE",
            GetFingerTipHandName(index),
            FingerTipNames[index],
            cursorActive,
            "cursorCreated=" + (cursor != null)
            + " cursorActive=" + cursorActive
            + " rendererEnabled=" + rendererEnabled
            + " scale=" + (cursor != null ? cursor.localScale.x.ToString("0.###") : "0")
            + " layer=" + (cursor != null ? cursor.gameObject.layer.ToString() : "Missing"));
    }

    private void LogFingerTipDebug(string eventName, string hand, string finger, bool tracked, string extra)
    {
        if (!enableFingerTipDebugLogs)
        {
            return;
        }

        Debug.Log("[NetworkUserAvatar] " + eventName
            + " avatarObjectId=" + GetAvatarObjectIdForLog()
            + " localPlayer=" + GetLocalPlayerForLog()
            + " inputAuthority=" + GetInputAuthorityForLog()
            + " stateAuthority=" + GetStateAuthorityForLog()
            + " isLocalAvatar=" + IsLocalAvatarForPoseSource()
            + " player=" + DisplayPlayerLabel
            + " role=" + CurrentRole
            + " robotTarget=" + RobotTarget
            + " deviceType=" + DeviceType
            + " hand=" + hand
            + " finger=" + finger
            + " tracked=" + tracked
            + " " + extra);
    }

    private void LogAvatarDebug(string message)
    {
        if (!enableAvatarDebugLogs)
        {
            return;
        }

        Debug.Log("[NetworkUserAvatar] " + message);
    }

    private void LogAvatarPoseSource(Transform poseSource)
    {
        if (!enableAvatarDebugLogs)
        {
            return;
        }

        bool isLocalAvatar = IsLocalAvatarForPoseSource();
        string cameraName = poseSource != null ? poseSource.name : "None";
        float now = Time.unscaledTime;
        if (avatarPoseSourceLogged
            && lastAvatarPoseSourceIsLocalAvatar == isLocalAvatar
            && string.Equals(lastAvatarPoseSourceCameraName, cameraName, System.StringComparison.Ordinal)
            && now - lastAvatarPoseSourceLogTime < 5f)
        {
            return;
        }

        avatarPoseSourceLogged = true;
        lastAvatarPoseSourceIsLocalAvatar = isLocalAvatar;
        lastAvatarPoseSourceCameraName = cameraName;
        lastAvatarPoseSourceLogTime = now;
        Debug.Log("[NetworkUserAvatar] PHOTON_AVATAR_POSE_SOURCE"
            + " avatarObjectId=" + GetAvatarObjectIdForLog()
            + " localPlayer=" + GetLocalPlayerForLog()
            + " inputAuthority=" + GetInputAuthorityForLog()
            + " stateAuthority=" + GetStateAuthorityForLog()
            + " isLocalAvatar=" + isLocalAvatar
            + " deviceType=" + DeviceType
            + " cameraName=" + cameraName);
    }

    private static string GetFingerTipHandName(int index)
    {
        return index < 5 ? "Left" : "Right";
    }

    private static string FormatVector(Vector3 value)
    {
        return value.ToString("F4");
    }

    private bool HasStateAuthorityForDebug()
    {
#if FUSION_WEAVER && FUSION2
        return HasStateAuthority;
#else
        return false;
#endif
    }

    private string GetAvatarObjectIdForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null && Object.Id.IsValid ? Object.Id.ToString() : "Invalid";
#else
        return "LocalOnly";
#endif
    }

    private string GetLocalPlayerForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Runner != null ? Runner.LocalPlayer.ToString() : "none";
#else
        return "none";
#endif
    }

    private string GetInputAuthorityForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null ? Object.InputAuthority.ToString() : "none";
#else
        return "none";
#endif
    }

    private string GetStateAuthorityForLog()
    {
#if FUSION_WEAVER && FUSION2
        return Object != null ? Object.StateAuthority.ToString() : "none";
#else
        return "none";
#endif
    }

    private bool ShouldUsePcObserverMainCameraPose()
    {
        bool fallbackPcObserver = PhotonSharedMRSessionSettings.IsPcObserverDevice(fallbackDeviceType)
            && fallbackRobotTarget == SharedMRRobotTarget.Observer
            && fallbackRole == SharedUserRole.Supervisor;
        return fallbackPcObserver || IsPcObserverAvatar;
    }

    private void LogPcObserverCameraMissingOnce()
    {
        if (pcObserverCameraMissingLogged)
        {
            return;
        }

        pcObserverCameraMissingLogged = true;
        Debug.LogWarning("[NetworkUserAvatar] PHOTON_PC_OBSERVER_CAMERA_MISSING"
            + " player=" + DisplayPlayerLabel
            + " role=" + CurrentRole
            + " robotTarget=" + RobotTarget
            + " deviceType=" + DeviceType);
    }

    private static SharedUserRole ClampRole(int roleValue)
    {
        if (roleValue < (int)SharedUserRole.ManipulatorOperator || roleValue > (int)SharedUserRole.Supervisor)
        {
            return SharedUserRole.ManipulatorOperator;
        }

        return (SharedUserRole)roleValue;
    }

    private static ShareDeviceType ClampDeviceType(int value)
    {
        if (!System.Enum.IsDefined(typeof(ShareDeviceType), value))
        {
            return ShareDeviceType.Unknown;
        }

        return (ShareDeviceType)value;
    }

    private static SharedMRRobotTarget ClampRobotTarget(int value)
    {
        if (!System.Enum.IsDefined(typeof(SharedMRRobotTarget), value))
        {
            return SharedMRRobotTarget.Observer;
        }

        return (SharedMRRobotTarget)value;
    }

#if FUSION_WEAVER && FUSION2
    private bool IsPcObserverMetadata()
    {
        ShareDeviceType deviceType = CanReadNetworkState ? ClampDeviceType(DeviceTypeValue) : fallbackDeviceType;
        SharedMRRobotTarget robotTarget = CanReadNetworkState ? ClampRobotTarget(RobotTargetValue) : fallbackRobotTarget;
        SharedUserRole role = CanReadNetworkState ? ClampRole(RoleValue) : fallbackRole;
        return PhotonSharedMRSessionSettings.IsPcObserverDevice(deviceType)
            && robotTarget == SharedMRRobotTarget.Observer
            && role == SharedUserRole.Supervisor;
    }

    private void ApplyResolvedDisplayNameToNetworkState()
    {
        if (Object == null || !HasStateAuthority)
        {
            return;
        }

        int observerDisplayNumber = CanReadNetworkState ? ObserverDisplayNumberValue : 0;
        if (observerDisplayNumber > 0)
        {
            UserNameValue = PhotonSharedMRSessionSettings.BuildObserverDisplayName(observerDisplayNumber);
            return;
        }

        SharedMRRobotTarget robotTarget = CanReadNetworkState ? ClampRobotTarget(RobotTargetValue) : fallbackRobotTarget;
        SharedUserRole role = CanReadNetworkState ? ClampRole(RoleValue) : fallbackRole;
        UserNameValue = PhotonSharedMRSessionSettings.BuildRobotDisplayName(
            robotTarget,
            role);
    }
#endif
}
