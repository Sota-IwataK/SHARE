using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public class HmdOverheadCursor : MonoBehaviour
{
    [Header("References")]
    public NetworkUserAvatar avatar;
    public Transform headAnchor;
    public TMP_Text labelText;
    public Renderer cursorRenderer;

    [Header("Layout")]
    [FormerlySerializedAs("verticalOffsetMeters")]
    public float cursorVerticalOffset = 0.25f;
    public float cursorScale = 0.035f;
    public float textScale = 0.12f;
    public float minVisibleDistance = 0f;
    public bool billboardToCamera = true;
    public bool showRobotTarget = true;
    public bool showHostLikeFlag = true;

    [Header("Role Colors")]
    public Color manipulatorColor = new Color(0.1f, 0.75f, 0.95f, 1f);
    public Color scoutColor = new Color(0.2f, 0.9f, 0.45f, 1f);
    public Color supervisorColor = new Color(1f, 0.74f, 0.2f, 1f);
    public Color unknownColor = Color.white;

    [Header("Debug")]
    public bool enableDebugLogs;

    private Material cursorMaterial;
    private bool roleFilterVisible = true;
    private bool networkReady;
    private bool waitingForAvatarSpawnLogged;
    private bool networkReadyLogged;
    private bool currentlyVisible;
    private string lastLabel;
    private SharedUserRole lastRole = (SharedUserRole)(-1);
    private float lastFacingDot;

    public bool IsCurrentlyVisible => currentlyVisible;
    public string CurrentLabel => lastLabel ?? string.Empty;
    public float LastFacingDot => lastFacingDot;

    private void Awake()
    {
        ResolveAvatar();
        EnsureVisuals();
    }

    private void OnEnable()
    {
        ResolveAvatar();
        RoleBasedInfoFilter filter = FindObjectOfType<RoleBasedInfoFilter>(true);
        if (filter != null)
        {
            roleFilterVisible = filter.CurrentHmdOverheadCursorsVisible;
        }

        SetVisible(false);
    }

    private void LateUpdate()
    {
        ResolveAvatar();
        if (avatar == null)
        {
            SetVisible(false);
            return;
        }

        if (!avatar.IsNetworkStateReady)
        {
            SetVisible(false);
            LogWaitingForAvatarSpawnOnce();
            return;
        }

        if (!networkReady)
        {
            NotifyAvatarNetworkSpawned();
        }

        Vector3 headPosition = headAnchor != null ? headAnchor.position : avatar.HeadWorldPosition;
        Transform viewTransform = NetworkUserAvatar.LocalViewTransform;
        bool visible = roleFilterVisible && !avatar.IsLocalUser && !avatar.IsPcObserverAvatar;
        if (visible && minVisibleDistance > 0f && viewTransform != null)
        {
            float distance = Vector3.Distance(viewTransform.position, headPosition);
            visible = distance >= minVisibleDistance;
        }

        SetVisible(visible);
        if (!visible)
        {
            return;
        }

        transform.position = headPosition + Vector3.up * cursorVerticalOffset;
        FaceViewTransform(viewTransform);
        RefreshLabelAndColor();
    }

    public void SetRoleFilterVisible(bool visible)
    {
        roleFilterVisible = visible;
        if (avatar == null || !avatar.IsNetworkStateReady)
        {
            SetVisible(false);
            return;
        }

        SetVisible(roleFilterVisible && !avatar.IsLocalUser && !avatar.IsPcObserverAvatar);
    }

    public void NotifyAvatarNetworkSpawned()
    {
        ResolveAvatar();
        networkReady = true;
        waitingForAvatarSpawnLogged = false;
        if (!networkReadyLogged)
        {
            networkReadyLogged = true;
            LogDebug("PHOTON_HMD_CURSOR_NETWORK_READY");
        }

        if (avatar != null && avatar.IsNetworkStateReady)
        {
            RefreshLabelAndColor();
        }
    }

    public void NotifyAvatarNetworkDespawned()
    {
        networkReady = false;
        networkReadyLogged = false;
        SetVisible(false);
    }

    private void ResolveAvatar()
    {
        if (avatar == null)
        {
            avatar = GetComponentInParent<NetworkUserAvatar>();
        }

        if (headAnchor == null && avatar != null)
        {
            headAnchor = avatar.HeadVisual;
        }
    }

    private void EnsureVisuals()
    {
        if (cursorRenderer == null)
        {
            GameObject cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursor.name = "CursorDot";
            cursor.transform.SetParent(transform, false);
            cursor.transform.localPosition = Vector3.zero;

            Collider collider = cursor.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            cursorRenderer = cursor.GetComponent<Renderer>();
        }

        if (cursorRenderer != null)
        {
            cursorRenderer.transform.localScale = Vector3.one * Mathf.Max(0.001f, cursorScale);
        }

        if (cursorRenderer != null && cursorMaterial == null)
        {
            cursorMaterial = CreateMaterial(unknownColor);
            cursorRenderer.sharedMaterial = cursorMaterial;
        }

        if (labelText == null)
        {
            GameObject label = new GameObject("Label", typeof(TextMeshPro));
            label.transform.SetParent(transform, false);

            labelText = label.GetComponent<TMP_Text>();
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 2.1f;
            labelText.enableWordWrapping = false;
            labelText.color = Color.white;
        }

        if (labelText != null)
        {
            labelText.transform.localPosition = Vector3.up * (Mathf.Max(0.001f, cursorScale) + 0.02f);
            labelText.transform.localScale = Vector3.one * Mathf.Max(0.001f, textScale);
        }
    }

    private void FaceViewTransform(Transform viewTransform)
    {
        if (!billboardToCamera || viewTransform == null)
        {
            lastFacingDot = 0f;
            return;
        }

        Vector3 forward = transform.position - viewTransform.position;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = viewTransform.forward;
        }

        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 toCamera = viewTransform.position - transform.position;
        lastFacingDot = toCamera.sqrMagnitude > 0.0001f
            ? Vector3.Dot(-transform.forward, toCamera.normalized)
            : 0f;
    }

    private void RefreshLabelAndColor()
    {
        if (avatar == null || !avatar.IsNetworkStateReady)
        {
            return;
        }

        SharedUserRole role = avatar.CurrentRole;
        string label = BuildLabel(avatar);
        if (labelText != null && label != lastLabel)
        {
            labelText.text = label;
            lastLabel = label;
        }

        if (role != lastRole)
        {
            Color color = GetRoleColor(role);
            if (cursorMaterial != null)
            {
                cursorMaterial.color = color;
            }

            if (labelText != null)
            {
                labelText.color = color;
            }

            lastRole = role;
        }
    }

    private string BuildLabel(NetworkUserAvatar targetAvatar)
    {
        string userName = string.IsNullOrWhiteSpace(targetAvatar.DisplayPlayerLabel)
            ? PhotonSharedMRSessionSettings.DefaultUserName
            : targetAvatar.DisplayPlayerLabel;
        string label = userName + "\n" + targetAvatar.CurrentRole;

        if (showRobotTarget)
        {
            label += "\n" + targetAvatar.RobotTarget;
        }

        return label;
    }

    private Color GetRoleColor(SharedUserRole role)
    {
        switch (role)
        {
            case SharedUserRole.ManipulatorOperator:
                return manipulatorColor;
            case SharedUserRole.Scout:
                return scoutColor;
            case SharedUserRole.Supervisor:
                return supervisorColor;
            default:
                return unknownColor;
        }
    }

    private void SetVisible(bool visible)
    {
        EnsureVisuals();
        currentlyVisible = visible;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = visible;
        }
    }

    private void LogWaitingForAvatarSpawnOnce()
    {
        if (waitingForAvatarSpawnLogged)
        {
            return;
        }

        waitingForAvatarSpawnLogged = true;
        LogDebug("PHOTON_HMD_CURSOR_WAITING_FOR_AVATAR_SPAWN");
    }

    private void LogDebug(string eventName)
    {
        if (!enableDebugLogs)
        {
            return;
        }

        Debug.Log("[HmdOverheadCursor] " + eventName
            + " avatar=" + (avatar != null ? avatar.name : "MissingAvatar")
            + " networkReady=" + (avatar != null && avatar.IsNetworkStateReady));
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
