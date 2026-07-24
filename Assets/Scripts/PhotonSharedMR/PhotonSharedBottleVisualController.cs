using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PhotonSharedBottleVisualController : MonoBehaviour
{
    public enum VisualState
    {
        Normal,
        LocalGrab,
        RemoteGrab,
        Stale
    }

    [Header("References")]
    public NetworkedSharedSceneObject sharedObject;

    [Header("Colors")]
    public Color normalTint = Color.white;
    public Color localGrabTint = new Color(1.0f, 0.72f, 0.18f, 1f);
    public Color remoteGrabTint = new Color(0.22f, 0.22f, 0.22f, 1f);
    public Color staleTint = new Color(0.36f, 0.44f, 0.50f, 1f);

    [Header("Debug")]
    public bool enableDebugLogs;

    private Renderer[] renderers;
    private MaterialPropertyBlock propertyBlock;
    private Color[] baseColors;
    private bool networkReady;
    private bool waitingForSpawnLogged;
    private VisualState lastState = (VisualState)(-1);

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public int RendererCount => renderers != null ? renderers.Length : 0;

    private void Awake()
    {
        ResolveReferences();
        CacheRenderers();
        networkReady = false;
        ApplyNormalVisual(true);
        LogDebug("PHOTON_BOTTLE_VISUAL_AWAKE networkReady=false");
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheRenderers();
        networkReady = false;
        waitingForSpawnLogged = false;
        ApplyNormalVisual(true);
    }

    private void OnDisable()
    {
        NotifyNetworkDespawned();
    }

    private void Update()
    {
        ResolveReferences();
        if (!networkReady)
        {
            LogWaitingForSpawn();
            return;
        }

        ApplyVisual(false);
    }

    public void NotifyNetworkSpawned()
    {
        ResolveReferences();
        CacheRenderers();
        networkReady = true;
        waitingForSpawnLogged = false;
        LogDebug("PHOTON_BOTTLE_VISUAL_SPAWNED networkReady=true");
        ApplyVisual(true);
    }

    public void NotifyNetworkDespawned()
    {
        networkReady = false;
        waitingForSpawnLogged = false;
    }

    private void ResolveReferences()
    {
        if (sharedObject == null)
        {
            sharedObject = GetComponent<NetworkedSharedSceneObject>();
        }
    }

    private void CacheRenderers()
    {
        Renderer[] discovered = GetComponentsInChildren<Renderer>(true);
        BottleTargetEffectController targetEffect = GetComponent<BottleTargetEffectController>();
        BottleOutOfRangeEffectController rangeEffect = GetComponent<BottleOutOfRangeEffectController>();
        List<Renderer> bottleRenderers = new List<Renderer>(discovered.Length);
        for (int i = 0; i < discovered.Length; i++)
        {
            Renderer renderer = discovered[i];
            if (renderer == null
                || (targetEffect != null
                    && targetEffect.EffectRoot != null
                    && renderer.transform.IsChildOf(targetEffect.EffectRoot))
                || (rangeEffect != null
                    && renderer.transform.name != null
                    && IsUnderNamedRoot(renderer.transform, "LocalOutOfRangeEffectRoot"))
                || IsUnderNamedRoot(renderer.transform, "LocalTargetConflictEffectRoot"))
            {
                continue;
            }
            bottleRenderers.Add(renderer);
        }
        renderers = bottleRenderers.ToArray();
        propertyBlock ??= new MaterialPropertyBlock();
        baseColors = new Color[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            Material material = renderer != null ? renderer.sharedMaterial : null;
            if (material != null && material.HasProperty(BaseColorId))
            {
                baseColors[i] = material.GetColor(BaseColorId);
            }
            else if (material != null && material.HasProperty(ColorId))
            {
                baseColors[i] = material.GetColor(ColorId);
            }
            else
            {
                baseColors[i] = Color.white;
            }
        }
    }

    private static bool IsUnderNamedRoot(Transform item, string rootName)
    {
        for (Transform current = item; current != null; current = current.parent)
        {
            if (current.name == rootName) return true;
        }
        return false;
    }

    private void ApplyNormalVisual(bool force)
    {
        ApplyTint(VisualState.Normal, force, false);
    }

    private void ApplyVisual(bool force)
    {
        ApplyTint(ResolveVisualState(), force, true);
    }

    private void ApplyTint(VisualState state, bool force, bool logState)
    {
        if (renderers == null || renderers.Length == 0)
        {
            CacheRenderers();
        }

        if (!force && state == lastState)
        {
            return;
        }

        lastState = state;
        Color tint = ResolveTint(state);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            Color color = MultiplyPreservingAlpha(baseColors[i], tint);
            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            propertyBlock.SetColor(EmissionColorId, Color.black);
            renderer.SetPropertyBlock(propertyBlock);
        }

        if (logState)
        {
            LogState(state);
        }
    }

    private VisualState ResolveVisualState()
    {
        if (!CanReadNetworkState())
        {
            return VisualState.Normal;
        }

        if (sharedObject.IsLockedByOther)
        {
            return VisualState.RemoteGrab;
        }

        if (sharedObject.IsLocalGrabActive)
        {
            return VisualState.LocalGrab;
        }

        PhotonSharedBottleDetectionVisualState detectionState = sharedObject.DetectionVisualState;
        if (detectionState == PhotonSharedBottleDetectionVisualState.Stale
            || detectionState == PhotonSharedBottleDetectionVisualState.Lost)
        {
            return VisualState.Stale;
        }

        return VisualState.Normal;
    }

    private bool CanReadNetworkState()
    {
        if (!networkReady || sharedObject == null)
        {
            return false;
        }

#if FUSION_WEAVER && FUSION2
        return sharedObject.Object != null && sharedObject.Object.Id.IsValid;
#else
        return true;
#endif
    }

    private Color ResolveTint(VisualState state)
    {
        switch (state)
        {
            case VisualState.LocalGrab:
                return localGrabTint;
            case VisualState.RemoteGrab:
                return remoteGrabTint;
            case VisualState.Stale:
                return staleTint;
            default:
                return normalTint;
        }
    }

    private void LogState(VisualState state)
    {
        if (!enableDebugLogs)
        {
            return;
        }

        string message;
        switch (state)
        {
            case VisualState.LocalGrab:
                message = "PHOTON_BOTTLE_VISUAL_LOCAL_GRAB";
                break;
            case VisualState.RemoteGrab:
                message = "PHOTON_BOTTLE_VISUAL_REMOTE_GRAB";
                break;
            case VisualState.Stale:
                message = "PHOTON_BOTTLE_VISUAL_STALE";
                break;
            default:
                message = "PHOTON_BOTTLE_VISUAL_NORMAL";
                break;
        }

        LogDebug(message);
    }

    private void LogWaitingForSpawn()
    {
        if (waitingForSpawnLogged)
        {
            return;
        }

        waitingForSpawnLogged = true;
        LogDebug("PHOTON_BOTTLE_VISUAL_WAITING_FOR_SPAWN");
    }

    private void LogDebug(string message)
    {
        if (!enableDebugLogs)
        {
            return;
        }

        Debug.Log("[PhotonSharedBottleVisualController] " + message + " object=" + name);
    }

    private static Color MultiplyPreservingAlpha(Color baseColor, Color tint)
    {
        return new Color(
            baseColor.r * tint.r,
            baseColor.g * tint.g,
            baseColor.b * tint.b,
            Mathf.Clamp01(baseColor.a * tint.a));
    }
}
