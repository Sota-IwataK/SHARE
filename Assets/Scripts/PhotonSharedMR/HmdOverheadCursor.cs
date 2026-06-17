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

    private Material cursorMaterial;
    private bool roleFilterVisible = true;
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
            SetRoleFilterVisible(filter.CurrentHmdOverheadCursorsVisible);
        }
    }

    private void LateUpdate()
    {
        ResolveAvatar();
        if (avatar == null)
        {
            SetVisible(false);
            return;
        }

        Vector3 headPosition = headAnchor != null ? headAnchor.position : avatar.HeadWorldPosition;
        Camera mainCamera = Camera.main;
        bool visible = roleFilterVisible && !avatar.IsLocalUser;
        if (visible && minVisibleDistance > 0f && mainCamera != null)
        {
            float distance = Vector3.Distance(mainCamera.transform.position, headPosition);
            visible = distance >= minVisibleDistance;
        }

        SetVisible(visible);
        if (!visible)
        {
            return;
        }

        transform.position = headPosition + Vector3.up * cursorVerticalOffset;
        FaceMainCamera(mainCamera);
        RefreshLabelAndColor();
    }

    public void SetRoleFilterVisible(bool visible)
    {
        roleFilterVisible = visible;
        SetVisible(roleFilterVisible && avatar != null && !avatar.IsLocalUser);
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

    private void FaceMainCamera(Camera camera)
    {
        if (!billboardToCamera || camera == null)
        {
            lastFacingDot = 0f;
            return;
        }

        Vector3 forward = transform.position - camera.transform.position;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = camera.transform.forward;
        }

        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 toCamera = camera.transform.position - transform.position;
        lastFacingDot = toCamera.sqrMagnitude > 0.0001f
            ? Vector3.Dot(-transform.forward, toCamera.normalized)
            : 0f;
    }

    private void RefreshLabelAndColor()
    {
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
        string userName = string.IsNullOrWhiteSpace(targetAvatar.CurrentUserName)
            ? PhotonSharedMRSessionSettings.DefaultUserName
            : targetAvatar.CurrentUserName;
        string label = userName + "\n" + targetAvatar.CurrentRole;

        if (showHostLikeFlag && targetAvatar.IsHostLikeUser)
        {
            label += " / Host-like";
        }

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
