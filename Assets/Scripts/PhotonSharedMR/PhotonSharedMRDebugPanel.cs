using System.Text;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PhotonSharedMRDebugPanel : MonoBehaviour
{
    [Header("Debug Panel")]
    public bool enableDebugPanel = true;
    public PhotonFusionSharedRoomBootstrap bootstrap;
    public RoleBasedInfoFilter roleFilter;
    public PhotonSharedBottleSpawner bottleSpawner;
    public PhotonDetectedBottleBridge detectedBottleBridge;

    [Header("Placement")]
    public Transform followTarget;
    public float panelDistance = 1.15f;
    public float panelVerticalOffset = -0.18f;
    public float panelScale = 0.025f;
    public bool billboardToCamera = true;

    [Header("Text")]
    public float refreshIntervalSeconds = 0.2f;
    public float fontSize = 2.0f;
    public Color textColor = new Color(0.45f, 1f, 0.75f, 1f);

    private readonly StringBuilder builder = new StringBuilder(512);
    private TMP_Text debugText;
    private float nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
        EnsureText();
        RefreshText();
    }

    private void OnEnable()
    {
        ResolveReferences();
        EnsureText();
        RefreshText();
    }

    private void LateUpdate()
    {
        EnsureText();
        if (debugText == null)
        {
            return;
        }

        debugText.gameObject.SetActive(enableDebugPanel);
        if (!enableDebugPanel)
        {
            return;
        }

        Transform target = ResolveFollowTarget();
        if (target != null)
        {
            transform.position = target.position
                + target.forward.normalized * Mathf.Max(0.05f, panelDistance)
                + Vector3.up * panelVerticalOffset;
            transform.localScale = Vector3.one * Mathf.Max(0.001f, panelScale);

            if (billboardToCamera)
            {
                Vector3 forward = transform.position - target.position;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = target.forward;
                }

                transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
        }

        if (Time.unscaledTime >= nextRefreshTime)
        {
            RefreshText();
            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.02f, refreshIntervalSeconds);
        }
    }

    private void ResolveReferences()
    {
        if (bootstrap == null)
        {
            EnsureBootstrap(nameof(ResolveReferences), false);
        }

        EnsureBootstrap(nameof(ResolveReferences), false);

        if (roleFilter == null)
        {
            roleFilter = FindObjectOfType<RoleBasedInfoFilter>(true);
        }

        if (bottleSpawner == null)
        {
            bottleSpawner = FindObjectOfType<PhotonSharedBottleSpawner>(true);
        }

        if (detectedBottleBridge == null)
        {
            detectedBottleBridge = FindObjectOfType<PhotonDetectedBottleBridge>(true);
        }
    }

    private PhotonFusionSharedRoomBootstrap EnsureBootstrap(string method, bool logIfMissing)
    {
        return PhotonSharedMRBootstrapResolver.EnsureBootstrap(ref bootstrap, this, method, logIfMissing);
    }

    private Transform ResolveFollowTarget()
    {
        if (followTarget != null)
        {
            return followTarget;
        }

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.transform : null;
    }

    private void EnsureText()
    {
        if (debugText == null)
        {
            GameObject textObject = new GameObject("DebugText", typeof(TextMeshPro));
            textObject.transform.SetParent(transform, false);
            textObject.transform.localPosition = Vector3.zero;
            textObject.transform.localRotation = Quaternion.identity;
            textObject.transform.localScale = Vector3.one;

            debugText = textObject.GetComponent<TMP_Text>();
            debugText.alignment = TextAlignmentOptions.TopLeft;
            debugText.enableWordWrapping = false;
            debugText.fontStyle = FontStyles.Bold;
        }

        debugText.fontSize = Mathf.Max(0.1f, fontSize);
        debugText.color = textColor;
    }

    private void RefreshText()
    {
        ResolveReferences();

        NetworkUserAvatar[] avatars = FindObjectsOfType<NetworkUserAvatar>(true);
        HmdOverheadCursor[] cursors = FindObjectsOfType<HmdOverheadCursor>(true);

        int localAvatarCount = 0;
        int remoteAvatarCount = 0;
        for (int i = 0; i < avatars.Length; i++)
        {
            NetworkUserAvatar avatar = avatars[i];
            if (avatar == null)
            {
                continue;
            }

            if (avatar.IsLocalUser)
            {
                localAvatarCount++;
            }
            else
            {
                remoteAvatarCount++;
            }
        }

        int visibleCursorCount = 0;
        for (int i = 0; i < cursors.Length; i++)
        {
            HmdOverheadCursor cursor = cursors[i];
            if (cursor != null && cursor.gameObject.activeInHierarchy && cursor.IsCurrentlyVisible)
            {
                visibleCursorCount++;
            }
        }

        builder.Length = 0;
        Append("RoomName", bootstrap != null ? bootstrap.DebugRoomName : "MissingBootstrap");
        Append("Joined", bootstrap != null && bootstrap.Joined);
        Append("Runner exists", bootstrap != null && bootstrap.RunnerExists);
        Append("Runner.IsRunning", bootstrap != null && bootstrap.RunnerIsRunning);
        Append("LocalPlayer", bootstrap != null ? bootstrap.LocalPlayerDebugText : "Unavailable");
        Append("ActivePlayers count", bootstrap != null ? bootstrap.ActivePlayersCount.ToString() : "0");
        Append("Spawned NetworkUserAvatar count", avatars.Length.ToString());
        Append("LocalAvatar count", localAvatarCount.ToString());
        Append("RemoteAvatar count", remoteAvatarCount.ToString());
        Append("HmdOverheadCursor visible count", visibleCursorCount.ToString());
        Append("SharedNetworkBottleCount", bottleSpawner != null ? bottleSpawner.SharedNetworkBottleCount.ToString() : "0");
        Append("LocalSpawnRequestCount", bottleSpawner != null ? bottleSpawner.LocalSpawnRequestCount.ToString() : "0");
        Append("RemoteSpawnObservedCount", bottleSpawner != null ? bottleSpawner.RemoteSpawnObservedCount.ToString() : "0");
        Append("LastSpawnedBottleNetworkId", bottleSpawner != null ? bottleSpawner.LastSpawnedBottleNetworkId : "MissingSpawner");
        Append("LastSpawnError", bottleSpawner != null ? EmptyAsNone(bottleSpawner.LastSpawnError) : "MissingSpawner");
        Append("Detection", detectedBottleBridge != null ? detectedBottleBridge.DetectionState : "MissingBridge");
        Append("DetectionAuthority", detectedBottleBridge != null && detectedBottleBridge.IsDetectionAuthority);
        Append("Detection source frame", detectedBottleBridge != null ? detectedBottleBridge.LastDetectionSourceFrame : "MissingBridge");
        Append("Unity world position", detectedBottleBridge != null ? detectedBottleBridge.LastDetectedWorldPosition.ToString("F3") : "MissingBridge");
        Append("Photon shared position", detectedBottleBridge != null ? detectedBottleBridge.LastSharedWorldPosition.ToString("F3") : "MissingBridge");
        Append("Coordinate alignment", detectedBottleBridge != null ? detectedBottleBridge.CoordinateAlignmentStatus : "MissingBridge");
        Append("Current Role", ResolveCurrentRole().ToString());
        Append("DeviceType", ResolveDeviceType().ToString());
        Append("FixedRegion", bootstrap != null ? bootstrap.DebugFixedRegion : "Unavailable");
        Append("Protocol", bootstrap != null ? bootstrap.DebugProtocol : "Unavailable");
        Append("LastJoinStatus", bootstrap != null ? bootstrap.LastJoinStatus : "MissingBootstrap");
        Append("LastError", bootstrap != null ? EmptyAsNone(bootstrap.LastError) : "MissingBootstrap");

        if (debugText != null)
        {
            debugText.text = builder.ToString();
        }
    }

    private SharedUserRole ResolveCurrentRole()
    {
        if (roleFilter != null)
        {
            return roleFilter.ActiveRole;
        }

        if (NetworkUserAvatar.Local != null)
        {
            return NetworkUserAvatar.Local.CurrentRole;
        }

        return bootstrap != null ? bootstrap.DebugCurrentRole : SharedUserRole.ManipulatorOperator;
    }

    private ShareDeviceType ResolveDeviceType()
    {
        if (NetworkUserAvatar.Local != null)
        {
            return NetworkUserAvatar.Local.DeviceType;
        }

        return bootstrap != null ? bootstrap.DebugDeviceType : ShareDeviceType.Unknown;
    }

    private void Append(string label, bool value)
    {
        Append(label, value ? "true" : "false");
    }

    private void Append(string label, string value)
    {
        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(value);
    }

    private static string EmptyAsNone(string value)
    {
        return string.IsNullOrEmpty(value) ? "None" : value;
    }
}
