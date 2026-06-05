using System.Reflection;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.Subsystems;
using RosMessageTypes.Std;
using TMPro;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR;

public class PalmHoverSendStartZone : MonoBehaviour
{
    private const float MissingLeftHandLogInterval = 2f;

    private enum SendZoneVisualState
    {
        Normal,
        Hovering,
        Activated
    }

    public Transform sendZoneAnchor;
    public GameObject anchorVisual;
    [Min(0.01f)] public float sendZoneRadius = 0.12f;
    public bool useAnchorAsSendZone = true;

    [Header("Hover")]
    [SerializeField, Min(0.01f)] private float requiredHoverTime = 0.5f;
    [SerializeField] private TrackedHandJoint hoverJoint = TrackedHandJoint.Palm;

    [Header("Send Start")]
    public MonoBehaviour sendStartReceiver;
    public string sendStartMethodName = "StartPalmPosePublishing";
    public UnityEvent sendStarted = new UnityEvent();

    [Header("Relative Anchor Reset")]
    [SerializeField] private bool publishResetRelativeAnchor = true;
    [SerializeField] private string resetRelativeAnchorTopic = "/amir_abs/reset_relative_anchor";
    [SerializeField, Min(0f)] private float resetRelativeAnchorCooldownSeconds = 0.5f;
    [SerializeField] private handPosePublisher palmPosePublisher;

    [Header("Visual State")]
    public bool showDebugVisuals = false;
    [SerializeField] private Color normalColor = new Color(1f, 0.92f, 0.12f, 1f);
    [SerializeField] private Color hoveringColor = new Color(0f, 0.85f, 1f, 1f);
    [SerializeField] private Color activatedColor = new Color(0f, 1f, 0.25f, 1f);
    [SerializeField, Min(0.01f)] private float normalScaleMultiplier = 1f;
    [SerializeField, Min(0.01f)] private float hoveringScaleMultiplier = 1.25f;
    [SerializeField, Min(0.01f)] private float activatedScaleMultiplier = 1.5f;

    public bool SendEnabled => activated;
    public float HoverTimer => hoverTimer;
    public float ResetRelativeAnchorCooldownSeconds
    {
        get => resetRelativeAnchorCooldownSeconds;
        set => resetRelativeAnchorCooldownSeconds = Mathf.Max(0f, value);
    }

    private const BindingFlags SendStartMethodFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static float nextResetRelativeAnchorAllowedTime;

    private ROSConnection ros;
    private string registeredResetTopic;
    private Renderer anchorRenderer;
    private Vector3 anchorVisualBaseScale = Vector3.one;
    private float hoverTimer;
    private bool hovering;
    private bool activated;
    private SendZoneVisualState currentVisualState = SendZoneVisualState.Normal;
    private float nextDebugVisualScanTime;
    private bool hasAppliedDebugVisualVisibility;
    private bool lastAppliedDebugVisualsVisible;
    private float nextMissingLeftHandLogTime;
    private bool loggedLeftHandOnlyMode;

    private void Awake()
    {
        CacheAnchorVisual();
        ApplyDebugVisualVisibility();
        SetVisualState(SendZoneVisualState.Normal, true);
    }

    private void OnEnable()
    {
        CacheAnchorVisual();
        ApplyDebugVisualVisibility();
        LogLeftHandOnlyModeOnce();
        SetVisualState(activated ? SendZoneVisualState.Activated : SendZoneVisualState.Normal, true);
    }

    private void Start()
    {
        LogLeftHandOnlyModeOnce();
        EnsureResetPublisher();
        ApplyDebugVisualVisibility();
    }

    private void Update()
    {
        ApplyDebugVisualVisibilityThrottled();

        if (activated)
        {
            SetVisualState(SendZoneVisualState.Activated);
            return;
        }

        if (!TryGetHandWorldPosition(out Vector3 handWorldPosition))
        {
            ResetHover();
            return;
        }

        Vector3 zoneCenter = GetSendZoneCenter();
        float distance = Vector3.Distance(handWorldPosition, zoneCenter);
        if (distance > sendZoneRadius)
        {
            ResetHover();
            return;
        }

        if (!hovering)
        {
            hovering = true;
            hoverTimer = 0f;
        }

        hoverTimer += Time.deltaTime;
        SetVisualState(SendZoneVisualState.Hovering);

        if (hoverTimer >= requiredHoverTime)
        {
            ActivateSendZone();
        }
    }

    private void OnValidate()
    {
        sendZoneRadius = Mathf.Max(0.01f, sendZoneRadius);
        requiredHoverTime = Mathf.Max(0.01f, requiredHoverTime);
        normalScaleMultiplier = Mathf.Max(0.01f, normalScaleMultiplier);
        hoveringScaleMultiplier = Mathf.Max(0.01f, hoveringScaleMultiplier);
        activatedScaleMultiplier = Mathf.Max(0.01f, activatedScaleMultiplier);
        if (string.IsNullOrWhiteSpace(resetRelativeAnchorTopic))
        {
            resetRelativeAnchorTopic = "/amir_abs/reset_relative_anchor";
        }
        resetRelativeAnchorCooldownSeconds = Mathf.Max(0f, resetRelativeAnchorCooldownSeconds);

        CacheAnchorVisual();
        ApplyDebugVisualVisibility();
    }

    public void ResetSendZone()
    {
        activated = false;
        ResetHover();
        SetVisualState(SendZoneVisualState.Normal, true);
    }

    public Vector3 GetSendZoneCenter()
    {
        if (useAnchorAsSendZone && sendZoneAnchor != null)
        {
            return sendZoneAnchor.position;
        }

        return transform.position;
    }

    private void ActivateSendZone()
    {
        activated = true;
        hovering = false;
        hoverTimer = 0f;
        SetVisualState(SendZoneVisualState.Activated, true);

        ResetPalmHmdRelativeAnchor();
        PublishResetRelativeAnchor();
        InvokeSendStart();
        sendStarted?.Invoke();
    }

    private void ResetHover()
    {
        hoverTimer = 0f;
        hovering = false;
        SetVisualState(SendZoneVisualState.Normal);
    }

    private bool TryGetHandWorldPosition(out Vector3 handWorldPosition)
    {
        handWorldPosition = Vector3.zero;

        var aggregator = XRSubsystemHelpers.HandsAggregator;
        if (aggregator == null)
        {
            LogMissingLeftHand("HandsAggregator is not available; RightHand fallback is disabled");
            return false;
        }

        if (!aggregator.TryGetJoint(hoverJoint, XRNode.LeftHand, out HandJointPose leftPose))
        {
            LogMissingLeftHand("LeftHand " + hoverJoint + " is not tracked; RightHand fallback is disabled");
            return false;
        }

        handWorldPosition = leftPose.Position;
        return true;
    }

    private void LogLeftHandOnlyModeOnce()
    {
        if (loggedLeftHandOnlyMode || !Application.isPlaying)
        {
            return;
        }

        loggedLeftHandOnlyMode = true;
        Debug.Log("[PalmHoverSendStartZone] SendZone hover uses XRNode.LeftHand only. RightHand fallback is disabled.");
    }

    private void LogMissingLeftHand(string reason)
    {
        if (!Application.isPlaying || Time.time < nextMissingLeftHandLogTime)
        {
            return;
        }

        nextMissingLeftHandLogTime = Time.time + MissingLeftHandLogInterval;
        Debug.Log("[PalmHoverSendStartZone] SendZone hover skipped: " + reason + ".");
    }

    private void InvokeSendStart()
    {
        if (sendStartReceiver == null || string.IsNullOrWhiteSpace(sendStartMethodName))
        {
            Debug.LogWarning("[PalmHoverSendStartZone] Send start receiver or method is not assigned.");
            return;
        }

        MethodInfo method = sendStartReceiver.GetType().GetMethod(sendStartMethodName, SendStartMethodFlags);
        if (method == null || method.GetParameters().Length != 0)
        {
            Debug.LogError("[PalmHoverSendStartZone] Send start method not found or has parameters: "
                + sendStartReceiver.GetType().Name + "." + sendStartMethodName);
            return;
        }

        method.Invoke(sendStartReceiver, null);
    }

    private void EnsureResetPublisher()
    {
        if (!Application.isPlaying || !publishResetRelativeAnchor || string.IsNullOrWhiteSpace(resetRelativeAnchorTopic))
        {
            return;
        }

        ros ??= ROSConnection.GetOrCreateInstance();
        if (ros == null || registeredResetTopic == resetRelativeAnchorTopic)
        {
            return;
        }

        ros.RegisterPublisher<EmptyMsg>(resetRelativeAnchorTopic);
        registeredResetTopic = resetRelativeAnchorTopic;
        Debug.Log("[PalmHoverSendStartZone] RegisterPublisher " + resetRelativeAnchorTopic);
    }

    private bool PublishResetRelativeAnchor()
    {
        return TryPublishResetRelativeAnchor(
            "PalmHoverSendStartZone",
            publishResetRelativeAnchor,
            resetRelativeAnchorTopic,
            resetRelativeAnchorCooldownSeconds,
            ref ros,
            ref registeredResetTopic);
    }

    public static bool TryPublishResetRelativeAnchor(
        string sourceName,
        bool shouldPublish,
        string topic,
        float cooldownSeconds,
        ref ROSConnection rosConnection,
        ref string registeredTopic)
    {
        if (!Application.isPlaying || !shouldPublish)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            Debug.LogWarning("[" + sourceName + "] reset_relative_anchor topic is empty.");
            return false;
        }

        if (Time.time < nextResetRelativeAnchorAllowedTime)
        {
            Debug.Log("[" + sourceName + "] Skipped " + topic + " publish due to cooldown.");
            return false;
        }

        if (rosConnection == null)
        {
            rosConnection = ROSConnection.GetOrCreateInstance();
        }

        if (rosConnection == null)
        {
            Debug.LogWarning("[" + sourceName + "] ROSConnection was not found; " + topic + " was not published.");
            return false;
        }

        if (registeredTopic != topic)
        {
            rosConnection.RegisterPublisher<EmptyMsg>(topic);
            registeredTopic = topic;
            Debug.Log("[" + sourceName + "] RegisterPublisher " + topic);
        }

        rosConnection.Publish(topic, new EmptyMsg());
        nextResetRelativeAnchorAllowedTime = Time.time + Mathf.Max(0f, cooldownSeconds);
        Debug.Log("[" + sourceName + "] Published " + topic);
        return true;
    }

    private void ResetPalmHmdRelativeAnchor()
    {
        handPosePublisher publisher = ResolvePalmPosePublisher();
        if (publisher == null)
        {
            Debug.LogWarning("[PalmHoverSendStartZone] handPosePublisher was not found; /palm_pose_hmd_relative anchor was not reset.");
            return;
        }

        if (!publisher.ResetPalmHmdAnchor())
        {
            Debug.LogWarning("[PalmHoverSendStartZone] Failed to reset /palm_pose_hmd_relative anchor at SendZone activation.");
        }
    }

    private handPosePublisher ResolvePalmPosePublisher()
    {
        if (palmPosePublisher != null)
        {
            return palmPosePublisher;
        }

        palmPosePublisher = FindObjectOfType<handPosePublisher>(true);
        return palmPosePublisher;
    }

    private void CacheAnchorVisual()
    {
        if (anchorVisual == null)
        {
            anchorRenderer = null;
            anchorVisualBaseScale = Vector3.one;
            return;
        }

        anchorRenderer = anchorVisual.GetComponent<Renderer>();
        if (anchorRenderer == null)
        {
            anchorRenderer = anchorVisual.GetComponentInChildren<Renderer>(true);
        }

        anchorVisualBaseScale = anchorVisual.transform.localScale;
        if (anchorVisualBaseScale == Vector3.zero)
        {
            anchorVisualBaseScale = Vector3.one * 0.08f;
        }
    }

    private void ApplyDebugVisualVisibilityThrottled()
    {
        if (!hasAppliedDebugVisualVisibility || lastAppliedDebugVisualsVisible != showDebugVisuals)
        {
            ApplyDebugVisualVisibility();
            return;
        }

        if (showDebugVisuals || Time.unscaledTime < nextDebugVisualScanTime)
        {
            return;
        }

        nextDebugVisualScanTime = Time.unscaledTime + 1f;
        ApplyDebugVisualVisibility();
    }

    private void ApplyDebugVisualVisibility()
    {
        SetAnchorVisualVisibility(showDebugVisuals);
        SetHmdStateDebugVisualsVisibility(showDebugVisuals);
        hasAppliedDebugVisualVisibility = true;
        lastAppliedDebugVisualsVisible = showDebugVisuals;
    }

    private void SetAnchorVisualVisibility(bool visible)
    {
        if (anchorVisual == null)
        {
            return;
        }

        foreach (Renderer renderer in anchorVisual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }

        foreach (Canvas canvas in anchorVisual.GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = visible;
        }

        foreach (Graphic graphic in anchorVisual.GetComponentsInChildren<Graphic>(true))
        {
            graphic.enabled = visible;
        }

        foreach (TMP_Text tmpText in anchorVisual.GetComponentsInChildren<TMP_Text>(true))
        {
            tmpText.enabled = visible;
        }

        foreach (UnityEngine.UI.Text uiText in anchorVisual.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            uiText.enabled = visible;
        }
    }

    private void SetHmdStateDebugVisualsVisibility(bool visible)
    {
        SetStateDebugVisualsVisibilityUnder(Camera.main != null ? Camera.main.transform : null, visible);

        foreach (Camera camera in Camera.allCameras)
        {
            SetStateDebugVisualsVisibilityUnder(camera.transform, visible);
        }

        SetStateDebugVisualsVisibilityUnder(FindActiveTransform("MainCamera"), visible);
        SetStateDebugVisualsVisibilityUnder(FindActiveTransform("CenterEyeAnchor"), visible);
        SetStateDebugVisualsVisibilityUnder(FindActiveTransform("HMD"), visible);
    }

    private Transform FindActiveTransform(string objectName)
    {
        GameObject found = GameObject.Find(objectName);
        return found != null ? found.transform : null;
    }

    private void SetStateDebugVisualsVisibilityUnder(Transform root, bool visible)
    {
        if (root == null)
        {
            return;
        }

        SetNamedDebugPanelsVisibility(root, visible);
        SetStateTextComponentsVisibility(root, visible);
    }

    private void SetNamedDebugPanelsVisibility(Transform root, bool visible)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            string objectName = child.gameObject.name;
            if (!objectName.Contains("DebugPanel") && !objectName.Contains("StateText"))
            {
                continue;
            }

            SetVisualComponentsVisibility(child.gameObject, visible);
        }
    }

    private void SetStateTextComponentsVisibility(Transform root, bool visible)
    {
        foreach (TMP_Text tmpText in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (StartsWithState(tmpText.text))
            {
                tmpText.enabled = visible;
            }
        }

        foreach (UnityEngine.UI.Text uiText in root.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            if (StartsWithState(uiText.text))
            {
                uiText.enabled = visible;
            }
        }
    }

    private bool StartsWithState(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith("STATE");
    }

    private void SetVisualComponentsVisibility(GameObject target, bool visible)
    {
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = visible;
        }

        foreach (Canvas canvas in target.GetComponentsInChildren<Canvas>(true))
        {
            canvas.enabled = visible;
        }

        foreach (Graphic graphic in target.GetComponentsInChildren<Graphic>(true))
        {
            graphic.enabled = visible;
        }

        foreach (TMP_Text tmpText in target.GetComponentsInChildren<TMP_Text>(true))
        {
            tmpText.enabled = visible;
        }

        foreach (UnityEngine.UI.Text uiText in target.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        {
            uiText.enabled = visible;
        }
    }

    private void SetVisualState(SendZoneVisualState state, bool force = false)
    {
        if (!force && currentVisualState == state)
        {
            return;
        }

        currentVisualState = state;
        if (anchorVisual == null)
        {
            return;
        }

        float scaleMultiplier = normalScaleMultiplier;
        Color color = normalColor;
        switch (state)
        {
            case SendZoneVisualState.Hovering:
                scaleMultiplier = hoveringScaleMultiplier;
                color = hoveringColor;
                break;
            case SendZoneVisualState.Activated:
                scaleMultiplier = activatedScaleMultiplier;
                color = activatedColor;
                break;
        }

        anchorVisual.transform.localScale = anchorVisualBaseScale * scaleMultiplier;
        SetRendererColor(color);
    }

    private void SetRendererColor(Color color)
    {
        if (anchorRenderer == null)
        {
            return;
        }

        Material material = Application.isPlaying ? anchorRenderer.material : anchorRenderer.sharedMaterial;
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        else if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = activated ? activatedColor : (hovering ? hoveringColor : normalColor);
        Gizmos.DrawWireSphere(GetSendZoneCenter(), sendZoneRadius);
    }
}
