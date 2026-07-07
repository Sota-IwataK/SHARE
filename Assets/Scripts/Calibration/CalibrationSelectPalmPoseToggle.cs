using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class CalibrationSelectPalmPoseToggle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private handPosePublisher amirPalmPosePublisher;
    [SerializeField] private TMP_Text buttonLabel;

    private const string OnLabel = "Left Hand Control: ON";
    private const string OffLabel = "Left Hand Control: OFF";
    private const string PendingLabel = "Left Hand Control: Preparing (3 s)";

    private bool loggedMissingPublisher;
    private bool loggedMissingLabel;
    private bool publisherResolvedAutomatically;
    private string resolvedPublisherSource = "<unresolved>";
    private bool lastLabelPublishEnabled;
    private bool lastLabelPending;

    private void Awake()
    {
        ResolveReferences(false);
    }

    private void OnEnable()
    {
        ResolveReferences(false);
        RefreshLabel();
    }

    private void Start()
    {
        ResolveReferences(true);
        RefreshLabel();
    }

    private void Update()
    {
        if (amirPalmPosePublisher == null || buttonLabel == null)
        {
            return;
        }

        bool publishEnabled = amirPalmPosePublisher.PublishEnabled;
        bool pending = amirPalmPosePublisher.IsPublishStartPending;
        if (publishEnabled != lastLabelPublishEnabled || pending != lastLabelPending)
        {
            RefreshLabel();
        }
    }

    public void ToggleLeftPalmPoseSending()
    {
        if (!ResolvePublisher(true))
        {
            return;
        }

        bool beforeEnabled = amirPalmPosePublisher.PublishEnabled;
        bool pending = amirPalmPosePublisher.IsPublishStartPending;
        string action;
        if (pending)
        {
            action = "CancelPending";
        }
        else if (beforeEnabled)
        {
            action = "Stop";
        }
        else
        {
            action = "StartDelay";
        }

        Debug.Log("[CalibrationSelectPalmPoseToggle] Toggle request:"
            + "\ntarget=" + amirPalmPosePublisher.name
            + "\npath=" + GetHierarchyPath(amirPalmPosePublisher.transform)
            + "\nbeforeEnabled=" + beforeEnabled
            + "\npending=" + pending
            + "\naction=" + action);

        if (pending || beforeEnabled)
        {
            amirPalmPosePublisher.StopLeftPalmPosePublish();
        }
        else
        {
            amirPalmPosePublisher.BeginLeftPalmPosePublishWithDelay();
        }

        RefreshLabel();
    }

    public void StartLeftPalmPoseSending()
    {
        if (!ResolvePublisher(true))
        {
            return;
        }

        amirPalmPosePublisher.BeginLeftPalmPosePublishWithDelay();
        RefreshLabel();
    }

    public void StopLeftPalmPoseSending()
    {
        if (!ResolvePublisher(true))
        {
            return;
        }

        amirPalmPosePublisher.StopLeftPalmPosePublish();
        RefreshLabel();
    }

    public void RefreshLabel()
    {
        ResolveLabel(false);
        if (buttonLabel == null)
        {
            LogMissingLabel();
            return;
        }

        if (amirPalmPosePublisher != null && amirPalmPosePublisher.IsPublishStartPending)
        {
            buttonLabel.text = PendingLabel;
            lastLabelPublishEnabled = amirPalmPosePublisher.PublishEnabled;
            lastLabelPending = true;
            return;
        }

        buttonLabel.text = amirPalmPosePublisher != null && amirPalmPosePublisher.PublishEnabled ? OnLabel : OffLabel;
        lastLabelPublishEnabled = amirPalmPosePublisher != null && amirPalmPosePublisher.PublishEnabled;
        lastLabelPending = false;
    }

    private void ResolveReferences(bool logErrors)
    {
        ResolvePublisher(logErrors);
        ResolveLabel(logErrors);
    }

    private bool ResolvePublisher(bool logErrors)
    {
        handPosePublisher explicitPublisher = publisherResolvedAutomatically ? null : amirPalmPosePublisher;
        handPosePublisher publisher = FindPalmPosePublisher(explicitPublisher, out string source);
        if (publisher != null)
        {
            amirPalmPosePublisher = publisher;
            publisherResolvedAutomatically = source != "Inspector";
            resolvedPublisherSource = source;
            Debug.Log("[CalibrationSelectPalmPoseToggle] Auto-resolved handPosePublisher: "
                + GetHierarchyPath(amirPalmPosePublisher.transform)
                + " source=" + resolvedPublisherSource);
            return true;
        }

        if (logErrors)
        {
            LogMissingPublisher();
        }

        return false;
    }

    private void ResolveLabel(bool logWarnings)
    {
        if (buttonLabel != null)
        {
            return;
        }

        buttonLabel = GetComponentInChildren<TMP_Text>(true);
        if (buttonLabel != null)
        {
            return;
        }

        Transform parent = transform.parent;
        while (parent != null && buttonLabel == null)
        {
            buttonLabel = parent.GetComponentInChildren<TMP_Text>(true);
            parent = parent.parent;
        }

        if (buttonLabel == null && logWarnings)
        {
            LogMissingLabel();
        }
    }

    private handPosePublisher FindPalmPosePublisher(handPosePublisher explicitPublisher, out string source)
    {
        if (explicitPublisher != null)
        {
            source = "Inspector";
            return explicitPublisher;
        }

        handPosePublisher rosTcpPublisher = FindRosTcpComponentsPublisher();
        if (rosTcpPublisher != null)
        {
            source = "RosTcpComponents";
            return rosTcpPublisher;
        }

        handPosePublisher activeInstance = handPosePublisher.ActiveInstance;
        if (activeInstance != null)
        {
            source = "ActiveInstance";
            return activeInstance;
        }

        handPosePublisher[] publishers = FindObjectsOfType<handPosePublisher>(true);
        handPosePublisher firstAny = null;
        handPosePublisher firstEnabledActive = null;
        for (int i = 0; i < publishers.Length; i++)
        {
            handPosePublisher publisher = publishers[i];
            if (publisher == null)
            {
                continue;
            }

            if (firstAny == null)
            {
                firstAny = publisher;
            }

            if (firstEnabledActive == null && IsActiveEnabledPublisher(publisher))
            {
                firstEnabledActive = publisher;
            }
        }

        if (firstEnabledActive != null)
        {
            source = "SceneSearch";
            return firstEnabledActive;
        }

        source = firstAny != null ? "SceneSearch" : "<unresolved>";
        return firstAny;
    }

    private static handPosePublisher FindRosTcpComponentsPublisher()
    {
        GameObject rosTcpComponents = GameObject.Find("RosTcpComponents");
        if (rosTcpComponents == null)
        {
            rosTcpComponents = FindSceneObjectByName("RosTcpComponents");
        }

        return rosTcpComponents != null ? rosTcpComponents.GetComponent<handPosePublisher>() : null;
    }

    private static GameObject FindSceneObjectByName(string objectName)
    {
        foreach (Transform candidate in FindObjectsOfType<Transform>(true))
        {
            if (candidate.gameObject.scene.IsValid() && candidate.name == objectName)
            {
                return candidate.gameObject;
            }
        }

        return null;
    }

    private static bool IsActiveEnabledPublisher(handPosePublisher publisher)
    {
        return publisher != null && publisher.enabled && publisher.gameObject.activeInHierarchy;
    }

    private void LogMissingPublisher()
    {
        if (loggedMissingPublisher)
        {
            return;
        }

        loggedMissingPublisher = true;
        handPosePublisher[] publishers = FindObjectsOfType<handPosePublisher>(true);
        Debug.LogError("[CalibrationSelectPalmPoseToggle] handPosePublisher was not found. "
            + "buttonObject=" + GetHierarchyPath(transform)
            + " searchedBy=RosTcpComponents, Inspector, ActiveInstance, enabled active scene instances, all scene instances"
            + " scenePublisherCount=" + publishers.Length
            + " publishers=" + FormatPublisherList(publishers));
    }

    private void LogMissingLabel()
    {
        if (loggedMissingLabel)
        {
            return;
        }

        loggedMissingLabel = true;
        Debug.LogWarning("[CalibrationSelectPalmPoseToggle] TMP_Text label was not found. "
            + "buttonObject=" + GetHierarchyPath(transform)
            + " searchedIn=self children and parent hierarchy. Toggle still works.");
    }

    private static string FormatPublisherList(handPosePublisher[] publishers)
    {
        if (publishers == null || publishers.Length == 0)
        {
            return "<none>";
        }

        string result = string.Empty;
        for (int i = 0; i < publishers.Length; i++)
        {
            handPosePublisher publisher = publishers[i];
            if (publisher == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(result))
            {
                result += "; ";
            }

            result += publisher.name
                + " path=" + GetHierarchyPath(publisher.transform)
                + " activeInHierarchy=" + publisher.gameObject.activeInHierarchy
                + " enabled=" + publisher.enabled
                + " PublishEnabled=" + publisher.PublishEnabled;
        }

        return string.IsNullOrEmpty(result) ? "<none>" : result;
    }

    private static string GetHierarchyPath(Transform target)
    {
        if (target == null)
        {
            return "<null>";
        }

        string path = target.name;
        Transform parent = target.parent;
        while (parent != null)
        {
            path = parent.name + "/" + path;
            parent = parent.parent;
        }

        return path;
    }
}
