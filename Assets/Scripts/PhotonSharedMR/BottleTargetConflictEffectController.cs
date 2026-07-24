using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BottleTargetConflictEffectController : MonoBehaviour
{
    [SerializeField] private float heightOffset = 0.12f;
    [SerializeField] private float fadeDuration = 0.15f;
    [SerializeField] private Transform hmdTransform;
    [SerializeField] private Color conflictColor = new Color(1f, 0.45f, 0.08f, 1f);
    [SerializeField] private bool enableDebugLogs = true;

    private Transform effectRoot;
    private TMP_Text ownerLabel;
    private float currentAlpha;
    private float startAlpha;
    private float targetAlpha;
    private float transitionStartedAt;
    private bool shown;

    private void Awake()
    {
        BuildEffect();
        effectRoot.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        effectRoot = null;
        ownerLabel = null;
        hmdTransform = null;
    }

    private void Update()
    {
        if (effectRoot == null || !effectRoot.gameObject.activeSelf) return;
        float progress = fadeDuration <= 0f
            ? 1f
            : Mathf.Clamp01((Time.unscaledTime - transitionStartedAt) / fadeDuration);
        currentAlpha = Mathf.Lerp(startAlpha, targetAlpha, progress);
        ownerLabel.alpha = currentAlpha;
        if (!shown && progress >= 1f)
        {
            effectRoot.gameObject.SetActive(false);
            return;
        }
        UpdateBillboard();
    }

    public void ShowConflict(string ownerDisplayName)
    {
        string resolvedName = string.IsNullOrWhiteSpace(ownerDisplayName)
            ? "UNKNOWN"
            : ownerDisplayName.ToUpperInvariant();
        ownerLabel.text = "TARGETED BY " + resolvedName;
        shown = true;
        BeginFade(1f);
        effectRoot.gameObject.SetActive(true);
        if (enableDebugLogs)
        {
            Debug.Log("[BottleTargetConflictEffectController] Conflict shown"
                + " networkId=" + NetworkIdText()
                + " ownerLabel=\"" + resolvedName + "\"", this);
        }
    }

    public void HideConflict()
    {
        if (!shown) return;
        shown = false;
        BeginFade(0f);
    }

    private void BeginFade(float alpha)
    {
        startAlpha = currentAlpha;
        targetAlpha = alpha;
        transitionStartedAt = Time.unscaledTime;
    }

    private void BuildEffect()
    {
        effectRoot = new GameObject("LocalTargetConflictEffectRoot").transform;
        effectRoot.SetParent(transform, false);
        Bounds bounds = ResolveBounds();
        effectRoot.position = new Vector3(bounds.center.x, bounds.max.y + heightOffset, bounds.center.z);

        GameObject labelObject = new GameObject("TargetOwnerLabel");
        labelObject.transform.SetParent(effectRoot, false);
        ownerLabel = labelObject.AddComponent<TextMeshPro>();
        ownerLabel.text = "TARGETED BY PLAYER";
        ownerLabel.alignment = TextAlignmentOptions.Center;
        ownerLabel.fontStyle = FontStyles.Bold;
        ownerLabel.fontSize = 1.6f;
        ownerLabel.color = conflictColor;
        ownerLabel.enableWordWrapping = false;
        labelObject.transform.localScale = Vector3.one * 0.035f;
    }

    private Bounds ResolveBounds()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(false);
        Bounds combined = new Bounds(transform.position, new Vector3(0.16f, 0.25f, 0.16f));
        bool initialized = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider item = colliders[i];
            if (item == null || !item.enabled) continue;
            if (!initialized) { combined = item.bounds; initialized = true; }
            else combined.Encapsulate(item.bounds);
        }
        return combined;
    }

    private void UpdateBillboard()
    {
        if (hmdTransform == null) hmdTransform = null;
        if (hmdTransform == null && Camera.main != null) hmdTransform = Camera.main.transform;
        if (hmdTransform == null || effectRoot == null) return;
        Vector3 direction = effectRoot.position - hmdTransform.position;
        if (direction.sqrMagnitude > 0.000001f)
        {
            effectRoot.rotation = Quaternion.LookRotation(direction.normalized, hmdTransform.up);
        }
    }

    private string NetworkIdText()
    {
        NetworkedSharedSceneObject bottle = GetComponent<NetworkedSharedSceneObject>();
        return bottle != null && bottle.Object != null && bottle.Object.Id.IsValid
            ? bottle.Object.Id.ToString()
            : "Invalid";
    }
}
