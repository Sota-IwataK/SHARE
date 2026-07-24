using System;
using Fusion;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BottleOutOfRangeEffectController : MonoBehaviour
{
    [Header("Effect Objects")]
    [SerializeField] private Transform warningRing;
    [SerializeField] private Transform warningBracketRoot;
    [SerializeField] private Transform warningMarker;
    [SerializeField] private TMP_Text outOfRangeLabel;
    [SerializeField] private Transform outOfRangeLabelRoot;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float pulseFrequency = 1.4f;
    [SerializeField, Min(0.01f)] private float pulseScaleMin = 0.95f;
    [SerializeField, Min(0.01f)] private float pulseScaleMax = 1.15f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.15f;

    [Header("Layout")]
    [SerializeField, Min(0.1f)] private float ringRadiusMultiplier = 1.35f;
    [SerializeField, Min(0f)] private float labelHeightOffset = 0.08f;
    [SerializeField] private bool billboardWarning = true;
    [SerializeField] private Transform hmdTransform;
    [SerializeField] private bool billboardLabelToHmd = true;
    [SerializeField] private bool smoothBillboardRotation = true;
    [SerializeField, Min(0f)] private float billboardRotationSpeed = 12f;
    [SerializeField] private Vector3 billboardRotationOffset = new Vector3(0f, 180f, 0f);
    [SerializeField] private Color warningColor = new Color(1f, 0.12f, 0.04f, 0.96f);

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;

    private const int RingSegments = 48;
    private static Material sharedWarningMaterial;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private NetworkedSharedSceneObject sharedObject;
    private Transform effectRoot;
    private Transform[] brackets;
    private Renderer[] effectRenderers;
    private Renderer labelRenderer;
    private MaterialPropertyBlock propertyBlock;
    private float baseRadius = 0.1f;
    private float labelLocalY = 0.25f;
    private float transitionStartedAt;
    private float transitionStartAlpha;
    private float targetAlpha;
    private float currentAlpha;
    private bool isLocallySelected;
    private bool hmdReacquiredLogged;

    public bool IsOutOfRange { get; private set; }
    public bool IsInsideOperationRange => !IsOutOfRange;
    public bool IsLocallySelected => isLocallySelected;

    public event Action<bool> RangeStateChanged;
    public event Action EnteredOperationRange;
    public event Action ExitedOperationRange;

    private void Awake()
    {
        sharedObject = GetComponent<NetworkedSharedSceneObject>();
        propertyBlock = new MaterialPropertyBlock();
        ResolveBounds(out Vector3 localCenter);
        BuildEffect(localCenter);
        effectRenderers = effectRoot.GetComponentsInChildren<Renderer>(true);
        effectRoot.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        if (effectRoot != null && effectRoot.gameObject != null)
        {
            effectRoot.gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        effectRoot = null;
        warningRing = null;
        warningBracketRoot = null;
        warningMarker = null;
        outOfRangeLabel = null;
        outOfRangeLabelRoot = null;
        hmdTransform = null;
        brackets = null;
        effectRenderers = null;
        RangeStateChanged = null;
        EnteredOperationRange = null;
        ExitedOperationRange = null;
    }

    private void Update()
    {
        if (effectRoot == null
            || effectRoot.gameObject == null
            || warningRing == null
            || warningBracketRoot == null
            || !effectRoot.gameObject.activeSelf)
        {
            return;
        }

        float now = Time.unscaledTime;
        float progress = fadeDuration <= 0f ? 1f : Mathf.Clamp01((now - transitionStartedAt) / fadeDuration);
        currentAlpha = Mathf.Lerp(transitionStartAlpha, targetAlpha, progress);
        if (!IsOutOfRange && progress >= 1f)
        {
            effectRoot.gameObject.SetActive(false);
            return;
        }

        float wave = (Mathf.Sin(now * Mathf.PI * 2f * pulseFrequency) + 1f) * 0.5f;
        float scale = Mathf.Lerp(pulseScaleMin, pulseScaleMax, wave);
        warningRing.localScale = Vector3.one * scale;
        warningBracketRoot.localScale = Vector3.one * Mathf.Lerp(0.98f, 1.04f, wave);
        UpdateBillboard();
        ApplyAlpha(currentAlpha);
    }

    public void SetOutOfRange(bool isOutOfRange)
    {
        if (IsOutOfRange == isOutOfRange) return;

        IsOutOfRange = isOutOfRange;
        transitionStartedAt = Time.unscaledTime;
        transitionStartAlpha = currentAlpha;
        targetAlpha = isOutOfRange ? 1f : 0f;
        if (isOutOfRange)
        {
            effectRoot.gameObject.SetActive(true);
            ExitedOperationRange?.Invoke();
            Log("[BottleOutOfRangeEffectController] Effect shown networkId=" + NetworkIdText());
        }
        else
        {
            EnteredOperationRange?.Invoke();
            Log("[BottleOutOfRangeEffectController] Effect cleared networkId=" + NetworkIdText());
        }
        RangeStateChanged?.Invoke(isOutOfRange);
        UpdateSelectedDetails();
    }

    public void SetLocallySelected(bool selected)
    {
        if (isLocallySelected == selected) return;
        isLocallySelected = selected;
        UpdateSelectedDetails();
        if (IsOutOfRange && enableDebugLogs)
        {
            Debug.Log("[BottleOutOfRangeEffectController] Label "
                + (selected ? "shown reason=SelectedOutOfRange" : "hidden reason=NotSelected")
                + " networkId=" + NetworkIdText(), this);
        }
    }

    private void ResolveBounds(out Vector3 localCenter)
    {
        Bounds bounds;
        if (!TryCombinedColliders(out bounds) && !TryCombinedRenderers(out bounds))
        {
            bounds = new Bounds(transform.position, new Vector3(0.16f, 0.25f, 0.16f));
        }

        localCenter = transform.InverseTransformPoint(bounds.center);
        float scale = Mathf.Max(0.0001f,
            Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)));
        baseRadius = Mathf.Max(0.035f,
            Mathf.Max(bounds.extents.x, bounds.extents.z) * ringRadiusMultiplier / scale);
        Vector3 localTop = transform.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.max.y + labelHeightOffset, bounds.center.z));
        labelLocalY = localTop.y - localCenter.y;
    }

    private bool TryCombinedColliders(out Bounds combined)
    {
        Collider[] items = GetComponentsInChildren<Collider>(false);
        combined = default;
        bool initialized = false;
        for (int i = 0; i < items.Length; i++)
        {
            Collider item = items[i];
            if (item == null || !item.enabled || IsTargetEffect(item.transform)) continue;
            if (!initialized) { combined = item.bounds; initialized = true; }
            else combined.Encapsulate(item.bounds);
        }
        return initialized;
    }

    private bool TryCombinedRenderers(out Bounds combined)
    {
        Renderer[] items = GetComponentsInChildren<Renderer>(false);
        combined = default;
        bool initialized = false;
        for (int i = 0; i < items.Length; i++)
        {
            Renderer item = items[i];
            if (item == null || !item.enabled || IsTargetEffect(item.transform)) continue;
            if (!initialized) { combined = item.bounds; initialized = true; }
            else combined.Encapsulate(item.bounds);
        }
        return initialized;
    }

    private static bool IsTargetEffect(Transform item)
    {
        for (Transform current = item; current != null; current = current.parent)
        {
            if (current.name == "LocalTargetEffectRoot") return true;
        }
        return false;
    }

    private void BuildEffect(Vector3 localCenter)
    {
        effectRoot = transform.Find("LocalOutOfRangeEffectRoot");
        if (effectRoot == null) effectRoot = NewChild(transform, "LocalOutOfRangeEffectRoot");
        effectRoot.localPosition = localCenter;
        effectRoot.localRotation = Quaternion.identity;
        effectRoot.localScale = Vector3.one;

        warningRing = CreateRing("WarningRing", baseRadius, 0.007f);
        warningBracketRoot = NewChild(effectRoot, "WarningBracketRoot");
        brackets = new[]
        {
            CreateBracket("WarningBracketTop", -1f, 1f),
            CreateBracket("WarningBracketRight", 1f, 1f),
            CreateBracket("WarningBracketBottom", 1f, -1f),
            CreateBracket("WarningBracketLeft", -1f, -1f)
        };
        float distance = baseRadius * 1.25f;
        brackets[0].localPosition = new Vector3(-distance, distance, 0f);
        brackets[1].localPosition = new Vector3(distance, distance, 0f);
        brackets[2].localPosition = new Vector3(distance, -distance, 0f);
        brackets[3].localPosition = new Vector3(-distance, -distance, 0f);

        warningMarker = NewChild(warningBracketRoot, "WarningMarker");
        LineRenderer marker = AddLine(warningMarker.gameObject, 0.006f, true);
        float markerSize = Mathf.Max(0.015f, baseRadius * 0.18f);
        marker.positionCount = 3;
        marker.SetPositions(new[]
        {
            new Vector3(0f, markerSize, 0f),
            new Vector3(markerSize, -markerSize, 0f),
            new Vector3(-markerSize, -markerSize, 0f)
        });
        warningMarker.localPosition = new Vector3(0f, labelLocalY - 0.035f, 0f);

        outOfRangeLabelRoot = NewChild(effectRoot, "OutOfRangeLabelRoot");
        outOfRangeLabelRoot.localPosition = new Vector3(0f, labelLocalY, 0f);
        Transform labelTransform = NewChild(outOfRangeLabelRoot, "OutOfRangeLabel");
        outOfRangeLabel = labelTransform.gameObject.AddComponent<TextMeshPro>();
        labelRenderer = outOfRangeLabel.GetComponent<Renderer>();
        outOfRangeLabel.text = "OUT OF RANGE";
        outOfRangeLabel.alignment = TextAlignmentOptions.Center;
        outOfRangeLabel.fontSize = 1.8f;
        outOfRangeLabel.fontStyle = FontStyles.Bold;
        outOfRangeLabel.color = warningColor;
        outOfRangeLabel.enableWordWrapping = false;
        labelTransform.localPosition = Vector3.zero;
        labelTransform.localScale = Vector3.one * 0.035f;
        warningBracketRoot.gameObject.SetActive(false);
    }

    private Transform CreateRing(string objectName, float radius, float width)
    {
        Transform ring = NewChild(effectRoot, objectName);
        LineRenderer line = AddLine(ring.gameObject, width, true);
        line.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / RingSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        return ring;
    }

    private Transform CreateBracket(string objectName, float xSign, float ySign)
    {
        Transform bracket = NewChild(warningBracketRoot, objectName);
        LineRenderer line = AddLine(bracket.gameObject, 0.006f, false);
        float length = Mathf.Max(0.018f, baseRadius * 0.28f);
        line.positionCount = 3;
        line.SetPositions(new[]
        {
            new Vector3(-xSign * length, 0f, 0f),
            Vector3.zero,
            new Vector3(0f, -ySign * length, 0f)
        });
        return bracket;
    }

    private static Transform NewChild(Transform parent, string objectName)
    {
        GameObject child = new GameObject(objectName);
        child.layer = parent.gameObject.layer;
        child.transform.SetParent(parent, false);
        return child.transform;
    }

    private static LineRenderer AddLine(GameObject target, float width, bool loop)
    {
        LineRenderer line = target.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = SharedWarningMaterial();
        return line;
    }

    private static Material SharedWarningMaterial()
    {
        if (sharedWarningMaterial != null) return sharedWarningMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        sharedWarningMaterial = new Material(shader)
        {
            name = "BottleOutOfRange_RuntimeShared",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedWarningMaterial;
    }

    private void UpdateBillboard()
    {
        if (!billboardWarning || !billboardLabelToHmd || outOfRangeLabelRoot == null) return;
        if (hmdTransform == null && Camera.main != null)
        {
            hmdTransform = Camera.main.transform;
            if (!hmdReacquiredLogged)
            {
                hmdReacquiredLogged = true;
                Log("[BottleOutOfRangeEffectController] HMD reacquired for OUT OF RANGE billboard");
            }
        }
        if (hmdTransform == null) return;

        Vector3 toHmd = hmdTransform.position - outOfRangeLabelRoot.position;
        if (toHmd.sqrMagnitude > 0.000001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toHmd.normalized, hmdTransform.up)
                * Quaternion.Euler(billboardRotationOffset);
            outOfRangeLabelRoot.rotation = smoothBillboardRotation
                ? Quaternion.Slerp(
                    outOfRangeLabelRoot.rotation,
                    targetRotation,
                    1f - Mathf.Exp(-billboardRotationSpeed * Time.unscaledDeltaTime))
                : targetRotation;
        }
    }

    private void ApplyAlpha(float alpha)
    {
        Color color = warningColor;
        color.a *= alpha;
        for (int i = 0; effectRenderers != null && i < effectRenderers.Length; i++)
        {
            Renderer renderer = effectRenderers[i];
            if (renderer == null || renderer == labelRenderer) continue;
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(ColorId, color);
            propertyBlock.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(propertyBlock);
        }
        outOfRangeLabel.alpha = isLocallySelected && IsOutOfRange ? color.a : 0f;
    }

    private string NetworkIdText()
    {
        NetworkObject networkObject = sharedObject != null ? sharedObject.Object : null;
        return networkObject != null && networkObject.Id.IsValid ? networkObject.Id.ToString() : "Invalid";
    }

    private void Log(string message)
    {
        if (enableDebugLogs) Debug.Log(message, this);
    }

    private void UpdateSelectedDetails()
    {
        if (warningBracketRoot != null)
        {
            warningBracketRoot.gameObject.SetActive(IsOutOfRange && isLocallySelected);
        }
    }
}
