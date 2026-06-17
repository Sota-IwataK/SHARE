using UnityEngine;

[DisallowMultipleComponent]
public class VirtualAmirStateDisplay : MonoBehaviour
{
    [Header("State")]
    public VirtualAmirStatus status = VirtualAmirStatus.Idle;
    [Range(0f, 1f)] public float taskProgress = 0.15f;
    [Range(0f, 1f)] public float riskLevel = 0.05f;
    public Vector3 endEffectorTargetLocal = new Vector3(0.35f, 0.55f, 0.45f);

    [Header("Visuals")]
    public bool autoBuildVisuals = true;
    public Transform robotRoot;
    public Transform baseVisual;
    public Transform shoulderVisual;
    public Transform elbowVisual;
    public Transform endEffectorVisual;
    public Transform graspAreaVisual;
    public LineRenderer armLine;
    public LineRenderer predictedTrajectoryLine;
    public TextMesh statusText;

    private void Awake()
    {
        if (autoBuildVisuals)
        {
            EnsureVisuals();
        }
    }

    private void Update()
    {
        EnsureVisuals();
        ApplyState(status, taskProgress, riskLevel, transform.TransformPoint(endEffectorTargetLocal));
    }

    public void ApplyState(VirtualAmirStatus nextStatus, float nextTaskProgress, float nextRiskLevel, Vector3 worldEndEffectorTarget)
    {
        status = nextStatus;
        taskProgress = Mathf.Clamp01(nextTaskProgress);
        riskLevel = Mathf.Clamp01(nextRiskLevel);

        if (robotRoot == null)
        {
            return;
        }

        Vector3 shoulder = robotRoot.position + robotRoot.up * 0.35f;
        Vector3 endEffector = worldEndEffectorTarget;
        Vector3 elbow = Vector3.Lerp(shoulder, endEffector, 0.5f) + robotRoot.up * 0.2f;

        SetPose(shoulderVisual, shoulder, Quaternion.identity);
        SetPose(elbowVisual, elbow, Quaternion.identity);
        SetPose(endEffectorVisual, endEffector, Quaternion.identity);
        SetPose(graspAreaVisual, endEffector, Quaternion.identity);

        if (armLine != null)
        {
            armLine.positionCount = 3;
            armLine.SetPosition(0, shoulder);
            armLine.SetPosition(1, elbow);
            armLine.SetPosition(2, endEffector);
        }

        if (predictedTrajectoryLine != null)
        {
            predictedTrajectoryLine.positionCount = 4;
            predictedTrajectoryLine.SetPosition(0, shoulder);
            predictedTrajectoryLine.SetPosition(1, Vector3.Lerp(shoulder, elbow, 0.7f));
            predictedTrajectoryLine.SetPosition(2, elbow);
            predictedTrajectoryLine.SetPosition(3, endEffector);
        }

        UpdateRiskVisual();
        UpdateStatusText();
    }

    private void EnsureVisuals()
    {
        if (robotRoot == null)
        {
            GameObject root = new GameObject("VirtualAmirVisualRoot");
            root.transform.SetParent(transform, false);
            robotRoot = root.transform;
        }

        if (baseVisual == null)
        {
            baseVisual = CreatePrimitive("VirtualAmirBase", PrimitiveType.Cube, robotRoot, new Vector3(0.35f, 0.08f, 0.35f), new Color(0.15f, 0.18f, 0.2f, 1f));
            baseVisual.localPosition = Vector3.zero;
        }

        if (shoulderVisual == null)
        {
            shoulderVisual = CreatePrimitive("VirtualAmirShoulder", PrimitiveType.Sphere, robotRoot, Vector3.one * 0.09f, new Color(0.2f, 0.55f, 1f, 1f));
        }

        if (elbowVisual == null)
        {
            elbowVisual = CreatePrimitive("VirtualAmirElbow", PrimitiveType.Sphere, robotRoot, Vector3.one * 0.08f, new Color(0.2f, 0.75f, 0.95f, 1f));
        }

        if (endEffectorVisual == null)
        {
            endEffectorVisual = CreatePrimitive("VirtualAmirEndEffector", PrimitiveType.Cube, robotRoot, new Vector3(0.09f, 0.05f, 0.12f), new Color(1f, 0.8f, 0.2f, 1f));
        }

        if (graspAreaVisual == null)
        {
            graspAreaVisual = CreatePrimitive("VirtualAmirGraspArea", PrimitiveType.Sphere, robotRoot, Vector3.one * 0.18f, new Color(0.1f, 1f, 0.5f, 0.3f));
        }

        if (armLine == null)
        {
            armLine = CreateLine("VirtualAmirArmLine", robotRoot, new Color(0.1f, 0.6f, 1f, 1f), 0.018f);
        }

        if (predictedTrajectoryLine == null)
        {
            predictedTrajectoryLine = CreateLine("VirtualAmirPredictedTrajectory", robotRoot, new Color(1f, 0.75f, 0.1f, 1f), 0.012f);
        }

        if (statusText == null)
        {
            GameObject textObject = new GameObject("VirtualAmirStatusText");
            textObject.transform.SetParent(robotRoot, false);
            textObject.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            statusText = textObject.AddComponent<TextMesh>();
            statusText.anchor = TextAnchor.MiddleCenter;
            statusText.alignment = TextAlignment.Center;
            statusText.characterSize = 0.045f;
            statusText.fontSize = 48;
            statusText.color = Color.white;
        }
    }

    private static Transform CreatePrimitive(string objectName, PrimitiveType primitiveType, Transform parent, Vector3 localScale, Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(primitiveType);
        obj.name = objectName;
        obj.transform.SetParent(parent, false);
        obj.transform.localScale = localScale;

        Collider collider = obj.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        Renderer renderer = obj.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateMaterial(color);
        }

        return obj.transform;
    }

    private static LineRenderer CreateLine(string objectName, Transform parent, Color color, float width)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(parent, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.widthMultiplier = width;
        line.sharedMaterial = CreateMaterial(color);
        return line;
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

    private void UpdateRiskVisual()
    {
        if (graspAreaVisual == null)
        {
            return;
        }

        Renderer renderer = graspAreaVisual.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        Color lowRisk = new Color(0.1f, 1f, 0.5f, 0.3f);
        Color highRisk = new Color(1f, 0.12f, 0.08f, 0.45f);
        renderer.sharedMaterial.color = Color.Lerp(lowRisk, highRisk, riskLevel);
    }

    private void UpdateStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        statusText.text = "Virtual Amir\n"
            + status + "\n"
            + "Task " + Mathf.RoundToInt(taskProgress * 100f) + "%  Risk " + Mathf.RoundToInt(riskLevel * 100f) + "%";

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            statusText.transform.rotation = Quaternion.LookRotation(statusText.transform.position - mainCamera.transform.position);
        }
    }

    private static void SetPose(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target != null)
        {
            target.SetPositionAndRotation(position, rotation);
        }
    }
}
