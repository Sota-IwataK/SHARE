using UnityEngine;

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
    public static NetworkUserAvatar Local { get; private set; }

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
    [SerializeField] private bool hideLocalVisuals = false;

#if FUSION_WEAVER && FUSION2
    [Networked] public NetworkString<_64> UserNameValue { get; set; }
    [Networked] public int RoleValue { get; set; }
    [Networked] public int DeviceTypeValue { get; set; }
    [Networked] public int RobotTargetValue { get; set; }
    [Networked] public NetworkBool IsHostLikeUserValue { get; set; }
    [Networked] public Vector3 HeadPosition { get; set; }
    [Networked] public Quaternion HeadRotation { get; set; }
    [Networked] public Vector3 LeftHandPosition { get; set; }
    [Networked] public Quaternion LeftHandRotation { get; set; }
    [Networked] public Vector3 RightHandPosition { get; set; }
    [Networked] public Quaternion RightHandRotation { get; set; }
#endif

    public string CurrentUserName
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return Object != null ? UserNameValue.ToString() : fallbackUserName;
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
            return ClampRole(RoleValue);
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
            return ClampDeviceType(DeviceTypeValue);
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
            return ClampRobotTarget(RobotTargetValue);
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
            return Object != null ? IsHostLikeUserValue : fallbackIsHostLikeUser;
#else
            return fallbackIsHostLikeUser;
#endif
        }
    }

    public bool IsLocalUser
    {
        get
        {
#if FUSION_WEAVER && FUSION2
            return Object != null && HasStateAuthority;
#else
            return Local == this;
#endif
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
            return Object != null ? HeadPosition : transform.position;
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
        }
#endif
    }

#if FUSION_WEAVER && FUSION2
    public override void Spawned()
    {
        EnsureVisuals();
        if (HasStateAuthority)
        {
            Local = this;
            ResolveSources();
            ApplyLocalFallbackMetadataToNetworkState();
            SampleLocalRigIntoNetworkState();
        }

        SetLocalVisualVisibility();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
        {
            return;
        }

        ResolveSources();
        SampleLocalRigIntoNetworkState();
    }

    public override void Render()
    {
        EnsureVisuals();
        ApplyNetworkStateToVisuals();
        SetLocalVisualVisibility();
    }
#else
    private void Awake()
    {
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

        if (headSource == null && Camera.main != null)
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
    }

    private void SampleLocalRigIntoNetworkState()
    {
        Transform resolvedHead = headSource != null ? headSource : transform;
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
    }

    private void ApplyNetworkStateToVisuals()
    {
        SetPose(headVisual, HeadPosition, HeadRotation);
        SetPose(leftHandVisual, LeftHandPosition, LeftHandRotation);
        SetPose(rightHandVisual, RightHandPosition, RightHandRotation);
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
        bool isLocal = Object != null && HasStateAuthority;
#else
        bool isLocal = Local == this;
#endif
        bool visible = !(isLocal && hideLocalVisuals);
        SetRendererVisibility(headVisual, visible);
        SetRendererVisibility(leftHandVisual, visible);
        SetRendererVisibility(rightHandVisual, visible);
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
        if (value < (int)ShareDeviceType.PCEditor || value > (int)ShareDeviceType.Unknown)
        {
            return ShareDeviceType.Unknown;
        }

        return (ShareDeviceType)value;
    }

    private static SharedMRRobotTarget ClampRobotTarget(int value)
    {
        if (value < (int)SharedMRRobotTarget.Amir || value > (int)SharedMRRobotTarget.Observer)
        {
            return SharedMRRobotTarget.Observer;
        }

        return (SharedMRRobotTarget)value;
    }
}
