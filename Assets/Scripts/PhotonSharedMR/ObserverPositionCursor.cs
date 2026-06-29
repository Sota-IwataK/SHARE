using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ObserverPositionCursor : MonoBehaviour
{
    [Header("References")]
    public NetworkUserAvatar avatar;
    public Transform headAnchor;
    public TMP_Text labelText;
    public Renderer cursorRenderer;

    [Header("Visual")]
    public Color cursorColor = Color.white;
    public float cursorScale = 0.04f;
    public float labelVerticalOffset = 0.06f;
    public float textScale = 0.10f;
    public bool billboardToCamera = true;

    [Header("Debug")]
    public bool enableDebugLogs;

    private Material cursorMaterial;
    private bool visualsCreatedLogged;
    private bool networkReady;
    private bool networkReadyLogged;
    private bool currentlyVisible;
    private string lastLabel;
    private float lastFacingDot;

    public bool IsCurrentlyVisible => currentlyVisible;
    public string CurrentLabel => lastLabel ?? string.Empty;
    public Color CurrentColor => cursorColor;
    public float LastFacingDot => lastFacingDot;

    private void Awake()
    {
        ResolveAvatar();
        EnsureVisuals();
        SetVisible(false);
    }

    private void LateUpdate()
    {
        ResolveAvatar();
        EnsureVisuals();

        if (avatar == null || !avatar.IsNetworkStateReady)
        {
            SetVisible(false);
            return;
        }

        if (!networkReady)
        {
            NotifyAvatarNetworkSpawned();
        }

        bool visible = avatar != null
            && avatar.IsPcObserverAvatar
            && !avatar.IsLocalUser;
        SetVisible(visible);
        if (!visible)
        {
            return;
        }

        Vector3 headPosition = avatar.HeadWorldPosition;
        transform.position = headPosition;
        FaceViewTransform(NetworkUserAvatar.LocalViewTransform);
        RefreshLabel();
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

    public void NotifyAvatarNetworkSpawned()
    {
        ResolveAvatar();
        networkReady = true;
        LogCreatedOnce();
        if (!networkReadyLogged)
        {
            networkReadyLogged = true;
            LogDebug("PHOTON_OBSERVER_CURSOR_NETWORK_READY", "finger=None tracked=False");
        }
    }

    public void NotifyAvatarNetworkDespawned()
    {
        networkReady = false;
        networkReadyLogged = false;
        SetVisible(false);
    }

    private void EnsureVisuals()
    {
        if (cursorRenderer == null)
        {
            GameObject cursor = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            cursor.name = "ObserverPositionCursorDot";
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
            if (cursorMaterial == null)
            {
                cursorMaterial = CreateMaterial(cursorColor);
                cursorRenderer.sharedMaterial = cursorMaterial;
            }

            cursorMaterial.color = cursorColor;
        }

        if (labelText == null)
        {
            GameObject label = new GameObject("ObserverPositionLabel", typeof(TextMeshPro));
            label.transform.SetParent(transform, false);
            labelText = label.GetComponent<TMP_Text>();
            labelText.alignment = TextAlignmentOptions.Center;
            labelText.fontSize = 2.0f;
            labelText.enableWordWrapping = false;
        }

        if (labelText != null)
        {
            labelText.transform.localPosition = Vector3.up * Mathf.Max(0.001f, labelVerticalOffset);
            labelText.transform.localScale = Vector3.one * Mathf.Max(0.001f, textScale);
            labelText.color = cursorColor;
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

    private void RefreshLabel()
    {
        if (labelText == null || avatar == null)
        {
            return;
        }

        if (!avatar.IsNetworkStateReady)
        {
            return;
        }

        string label = avatar.DisplayPlayerLabel + "\n" + avatar.CurrentRole;
        if (label == lastLabel)
        {
            return;
        }

        labelText.text = label;
        lastLabel = label;
    }

    private void SetVisible(bool visible)
    {
        if (currentlyVisible == visible)
        {
            return;
        }

        currentlyVisible = visible;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = visible;
            }
        }

        LogVisibleChanged(visible);
    }

    private void LogCreatedOnce()
    {
        if (visualsCreatedLogged)
        {
            return;
        }

        visualsCreatedLogged = true;
        LogDebug("PHOTON_OBSERVER_CURSOR_CREATED", "finger=None tracked=False");
    }

    private void LogVisibleChanged(bool visible)
    {
        LogDebug("PHOTON_OBSERVER_CURSOR_VISIBLE", "finger=None tracked=" + visible);
    }

    private void LogDebug(string eventName, string extra)
    {
        if (!enableDebugLogs)
        {
            return;
        }

        Debug.Log("[ObserverPositionCursor] " + eventName
            + " player=" + (avatar != null && avatar.IsNetworkStateReady ? avatar.DisplayPlayerLabel : "MissingAvatar")
            + " role=" + (avatar != null && avatar.IsNetworkStateReady ? avatar.CurrentRole.ToString() : "MissingRole")
            + " robotTarget=" + (avatar != null && avatar.IsNetworkStateReady ? avatar.RobotTarget.ToString() : "MissingRobotTarget")
            + " deviceType=" + (avatar != null && avatar.IsNetworkStateReady ? avatar.DeviceType.ToString() : "MissingDeviceType")
            + " " + extra);
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
