using Fusion;
using UnityEngine;

public enum LocalTargetEffectState
{
    Normal,
    Candidate,
    Selected
}

[DisallowMultipleComponent]
public sealed class BottleTargetEffectController : MonoBehaviour
{
    [Header("Effect Objects")]
    [SerializeField] private Transform candidateRing;
    [SerializeField] private Transform selectedRing;
    [SerializeField] private Transform bracketRoot;
    [SerializeField] private Transform selectedMarker;
    [SerializeField] private Transform confirmationWave;

    [Header("Candidate")]
    [SerializeField, Min(0f)] private float candidatePulseFrequency = 1.5f;
    [SerializeField, Min(0.01f)] private float candidateMinScale = 0.90f;
    [SerializeField, Min(0.01f)] private float candidateMaxScale = 1.12f;
    [SerializeField, Min(0f)] private float candidateBracketTravel = 0.03f;

    [Header("Selected")]
    [SerializeField, Min(0.01f)] private float selectedConfirmationDuration = 0.25f;
    [SerializeField, Min(1f)] private float selectedConfirmationScale = 1.45f;
    [SerializeField, Min(0f)] private float selectedIdlePulseFrequency = 0.6f;

    [Header("Layout")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.15f;
    [SerializeField] private bool autoSizeFromBounds = true;
    [SerializeField, Min(0.1f)] private float ringRadiusMultiplier = 1.25f;
    [SerializeField] private bool billboardBrackets = true;
    [SerializeField] private Transform hmdTransform;

    [Header("Colors")]
    [SerializeField] private Color candidateColor = new Color(1f, 0.82f, 0.15f, 0.88f);
    [SerializeField] private Color selectedColor = new Color(0.20f, 1f, 0.42f, 0.96f);
    [SerializeField] private Color selectedOutOfRangeColor = new Color(1f, 0.12f, 0.08f, 1f);

    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs;

    private const int RingSegments = 48;
    private static Material sharedEffectMaterial;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private NetworkedSharedSceneObject sharedObject;
    private Transform effectRoot;
    private Transform[] bracketParts;
    private Renderer[] effectRenderers;
    private MaterialPropertyBlock propertyBlock;
    private LocalTargetEffectState currentState;
    private LocalTargetEffectState renderedState;
    private float stateChangedAt;
    private float fadeStartedAt = -1f;
    private float baseRadius = 0.1f;
    private float markerLocalY = 0.2f;
    private float currentAlpha;
    private bool isOutOfRange;

    public LocalTargetEffectState CurrentState => currentState;
    public Transform EffectRoot => effectRoot;

    private void Awake()
    {
        sharedObject = GetComponent<NetworkedSharedSceneObject>();
        propertyBlock = new MaterialPropertyBlock();
        ResolveEffectBounds(out Vector3 localCenter);
        EnsureEffectObjects(localCenter);
        CacheEffectRenderers();
        ApplyVisibility(false);
    }

    private void OnEnable()
    {
        if (effectRoot != null)
        {
            ApplyVisibility(currentState != LocalTargetEffectState.Normal);
        }
    }

    private void OnDisable()
    {
        ApplyVisibility(false);
    }

    private void OnDestroy()
    {
        effectRoot = null;
        candidateRing = null;
        selectedRing = null;
        bracketRoot = null;
        selectedMarker = null;
        confirmationWave = null;
        hmdTransform = null;
        bracketParts = null;
        effectRenderers = null;
    }

    private void Update()
    {
        if (effectRoot == null
            || effectRoot.gameObject == null
            || bracketRoot == null
            || renderedState == LocalTargetEffectState.Normal)
        {
            return;
        }

        float now = Time.unscaledTime;
        UpdateBillboard();
        if (renderedState == LocalTargetEffectState.Candidate)
        {
            UpdateCandidate(now);
        }
        else
        {
            UpdateSelected(now);
        }

        UpdateFade(now);
        ApplyAlpha(currentAlpha);
    }

    public void SetLocalTargetState(LocalTargetEffectState state)
    {
        if (currentState == state)
        {
            return;
        }

        LocalTargetEffectState old = currentState;
        currentState = state;
        stateChangedAt = Time.unscaledTime;

        if (state == LocalTargetEffectState.Normal)
        {
            fadeStartedAt = stateChangedAt;
            Log("[BottleTargetEffectController] Effect cleared networkId=" + NetworkIdText());
            return;
        }

        renderedState = state;
        fadeStartedAt = -1f;
        currentAlpha = 1f;
        ConfigureStateObjects(state);
        ApplyVisibility(true);
        Log("[BottleTargetEffectController] Effect changed networkId=" + NetworkIdText()
            + " old=" + old + " new=" + state);

        if (state == LocalTargetEffectState.Selected)
        {
            confirmationWave.gameObject.SetActive(true);
            confirmationWave.localScale = Vector3.one;
            Log("[BottleTargetEffectController] Selected confirmation played networkId=" + NetworkIdText());
        }
    }

    public void SetOutOfRange(bool outOfRange)
    {
        if (isOutOfRange == outOfRange) return;
        isOutOfRange = outOfRange;
        if (currentState == LocalTargetEffectState.Selected)
        {
            ApplyAlpha(currentAlpha);
            Log("[BottleTargetEffectController] Selected effect changed to "
                + (outOfRange ? "out-of-range" : "in-range")
                + " networkId=" + NetworkIdText());
        }
    }

    private void ResolveEffectBounds(out Vector3 localCenter)
    {
        Bounds bounds;
        if (!autoSizeFromBounds || !TryGetCombinedColliderBounds(out bounds))
        {
            if (!autoSizeFromBounds || !TryGetCombinedRendererBounds(out bounds))
            {
                bounds = new Bounds(transform.position, new Vector3(0.16f, 0.25f, 0.16f));
            }
        }

        localCenter = transform.InverseTransformPoint(bounds.center);
        float worldRadius = Mathf.Max(bounds.extents.x, bounds.extents.z) * ringRadiusMultiplier;
        float horizontalScale = Mathf.Max(0.0001f, Mathf.Max(
            Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)));
        baseRadius = Mathf.Max(0.035f, worldRadius / horizontalScale);

        Vector3 localTop = transform.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.max.y + 0.05f, bounds.center.z));
        markerLocalY = localTop.y - localCenter.y;
    }

    private bool TryGetCombinedColliderBounds(out Bounds combined)
    {
        Collider[] items = GetComponentsInChildren<Collider>(false);
        bool initialized = false;
        combined = default;
        for (int i = 0; i < items.Length; i++)
        {
            Collider item = items[i];
            if (item == null || !item.enabled) continue;
            if (!initialized) { combined = item.bounds; initialized = true; }
            else combined.Encapsulate(item.bounds);
        }
        return initialized;
    }

    private bool TryGetCombinedRendererBounds(out Bounds combined)
    {
        Renderer[] items = GetComponentsInChildren<Renderer>(false);
        bool initialized = false;
        combined = default;
        for (int i = 0; i < items.Length; i++)
        {
            Renderer item = items[i];
            if (item == null || !item.enabled || (effectRoot != null && item.transform.IsChildOf(effectRoot)))
            {
                continue;
            }
            if (!initialized) { combined = item.bounds; initialized = true; }
            else combined.Encapsulate(item.bounds);
        }
        return initialized;
    }

    private void EnsureEffectObjects(Vector3 localCenter)
    {
        effectRoot = transform.Find("LocalTargetEffectRoot");
        if (effectRoot == null)
        {
            effectRoot = NewChild(transform, "LocalTargetEffectRoot");
        }
        effectRoot.localPosition = localCenter;
        effectRoot.localRotation = Quaternion.identity;
        effectRoot.localScale = Vector3.one;

        candidateRing = EnsureRing(candidateRing, "CandidateRing", baseRadius, 0.004f);
        selectedRing = EnsureRing(selectedRing, "SelectedRing", baseRadius, 0.007f);
        confirmationWave = EnsureRing(confirmationWave, "ConfirmationWave", baseRadius, 0.006f);

        if (bracketRoot == null)
        {
            bracketRoot = NewChild(effectRoot, "BracketRoot");
        }
        bracketParts = new[]
        {
            EnsureBracket("TargetBracketTop", 0),
            EnsureBracket("TargetBracketRight", 1),
            EnsureBracket("TargetBracketBottom", 2),
            EnsureBracket("TargetBracketLeft", 3)
        };

        if (selectedMarker == null)
        {
            selectedMarker = NewChild(bracketRoot, "SelectedMarker");
            LineRenderer markerLine = AddLine(selectedMarker.gameObject, 0.005f, false);
            markerLine.positionCount = 5;
            float size = Mathf.Max(0.012f, baseRadius * 0.18f);
            markerLine.SetPositions(new[]
            {
                new Vector3(0f, size, 0f), new Vector3(size, 0f, 0f),
                new Vector3(0f, -size, 0f), new Vector3(-size, 0f, 0f),
                new Vector3(0f, size, 0f)
            });
        }
        selectedMarker.localPosition = new Vector3(0f, markerLocalY, 0f);
    }

    private Transform EnsureRing(Transform existing, string objectName, float radius, float width)
    {
        Transform ring = existing != null ? existing : effectRoot.Find(objectName);
        if (ring == null) ring = NewChild(effectRoot, objectName);
        LineRenderer line = ring.GetComponent<LineRenderer>();
        if (line == null) line = AddLine(ring.gameObject, width, true);
        line.positionCount = RingSegments;
        line.loop = true;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / RingSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
        return ring;
    }

    private Transform EnsureBracket(string objectName, int quadrant)
    {
        Transform bracket = bracketRoot.Find(objectName);
        if (bracket == null) bracket = NewChild(bracketRoot, objectName);
        LineRenderer line = bracket.GetComponent<LineRenderer>();
        if (line == null) line = AddLine(bracket.gameObject, 0.005f, false);
        float length = Mathf.Max(0.015f, baseRadius * 0.25f);
        float xSign = quadrant == 1 || quadrant == 2 ? 1f : -1f;
        float ySign = quadrant < 2 ? 1f : -1f;
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
        Transform childTransform = child.transform;
        childTransform.SetParent(parent, false);
        return childTransform;
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
        line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        line.sharedMaterial = GetSharedEffectMaterial();
        return line;
    }

    private static Material GetSharedEffectMaterial()
    {
        if (sharedEffectMaterial != null) return sharedEffectMaterial;
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        sharedEffectMaterial = new Material(shader)
        {
            name = "BottleTargetEffect_RuntimeShared",
            hideFlags = HideFlags.HideAndDontSave
        };
        return sharedEffectMaterial;
    }

    private void CacheEffectRenderers()
    {
        effectRenderers = effectRoot.GetComponentsInChildren<Renderer>(true);
    }

    private void ConfigureStateObjects(LocalTargetEffectState state)
    {
        candidateRing.gameObject.SetActive(state == LocalTargetEffectState.Candidate);
        selectedRing.gameObject.SetActive(state == LocalTargetEffectState.Selected);
        selectedMarker.gameObject.SetActive(state == LocalTargetEffectState.Selected);
        confirmationWave.gameObject.SetActive(false);
        bracketRoot.gameObject.SetActive(true);
    }

    private void UpdateCandidate(float now)
    {
        float wave = (Mathf.Sin((now - stateChangedAt) * Mathf.PI * 2f * candidatePulseFrequency) + 1f) * 0.5f;
        float scale = Mathf.Lerp(candidateMinScale, candidateMaxScale, wave);
        candidateRing.localScale = Vector3.one * scale;
        PositionBrackets(baseRadius * 1.35f + candidateBracketTravel * (1f - wave));
    }

    private void UpdateSelected(float now)
    {
        float wave = (Mathf.Sin((now - stateChangedAt) * Mathf.PI * 2f * selectedIdlePulseFrequency) + 1f) * 0.5f;
        selectedRing.localScale = Vector3.one * Mathf.Lerp(0.98f, 1.02f, wave);
        PositionBrackets(baseRadius * 1.22f);

        float confirmationElapsed = now - stateChangedAt;
        if (confirmationElapsed <= selectedConfirmationDuration)
        {
            float progress = Mathf.Clamp01(confirmationElapsed / selectedConfirmationDuration);
            confirmationWave.gameObject.SetActive(true);
            confirmationWave.localScale = Vector3.one * Mathf.Lerp(1f, selectedConfirmationScale, progress);
            ApplyRendererAlpha(
                confirmationWave.GetComponent<Renderer>(),
                1f - progress,
                isOutOfRange ? selectedOutOfRangeColor : selectedColor);
        }
        else if (confirmationWave.gameObject.activeSelf)
        {
            confirmationWave.gameObject.SetActive(false);
        }
    }

    private void PositionBrackets(float distance)
    {
        if (bracketParts == null || bracketParts.Length != 4) return;
        bracketParts[0].localPosition = new Vector3(-distance, distance, 0f);
        bracketParts[1].localPosition = new Vector3(distance, distance, 0f);
        bracketParts[2].localPosition = new Vector3(distance, -distance, 0f);
        bracketParts[3].localPosition = new Vector3(-distance, -distance, 0f);
    }

    private void UpdateBillboard()
    {
        if (!billboardBrackets || bracketRoot == null) return;
        if (hmdTransform == null) hmdTransform = null;
        if (hmdTransform == null && Camera.main != null) hmdTransform = Camera.main.transform;
        if (hmdTransform == null) return;
        Vector3 direction = bracketRoot.position - hmdTransform.position;
        if (direction.sqrMagnitude > 0.000001f)
        {
            bracketRoot.rotation = Quaternion.LookRotation(direction.normalized, hmdTransform.up);
        }
    }

    private void UpdateFade(float now)
    {
        if (currentState != LocalTargetEffectState.Normal || fadeStartedAt < 0f)
        {
            currentAlpha = 1f;
            return;
        }

        float progress = fadeOutDuration <= 0f ? 1f : (now - fadeStartedAt) / fadeOutDuration;
        currentAlpha = 1f - Mathf.Clamp01(progress);
        if (progress >= 1f)
        {
            renderedState = LocalTargetEffectState.Normal;
            fadeStartedAt = -1f;
            ApplyVisibility(false);
        }
    }

    private void ApplyVisibility(bool visible)
    {
        if (effectRoot != null) effectRoot.gameObject.SetActive(visible);
    }

    private void ApplyAlpha(float alpha)
    {
        Color color = renderedState == LocalTargetEffectState.Selected
            ? (isOutOfRange ? selectedOutOfRangeColor : selectedColor)
            : candidateColor;
        for (int i = 0; effectRenderers != null && i < effectRenderers.Length; i++)
        {
            Renderer renderer = effectRenderers[i];
            if (renderer == null || renderer.transform == confirmationWave) continue;
            ApplyRendererAlpha(renderer, alpha, color);
        }
    }

    private void ApplyRendererAlpha(Renderer renderer, float alpha, Color color)
    {
        if (renderer == null) return;
        color.a *= Mathf.Clamp01(alpha);
        renderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetColor(ColorId, color);
        propertyBlock.SetColor(BaseColorId, color);
        renderer.SetPropertyBlock(propertyBlock);
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
}
