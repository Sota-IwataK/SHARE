#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConfigureCalibrationFlowScene
{
    private const string PalmAnchorMarkerName = "PalmAnchorMarker";
    private const string SendZoneObjectName = "PalmHoverSendStartZone";
    private const float PalmAnchorMarkerDiameter = 0.08f;
    private const float SendZoneRadius = 0.12f;
    private const float ResetRelativeAnchorCooldownSeconds = 0.5f;
    private const bool ShowDebugVisuals = false;

    [MenuItem("Amir/Configure Calibration Flow Scene")]
    public static void Apply()
    {
        GameObject marker = GetOrCreateSphere(PalmAnchorMarkerName);
        marker.transform.position = ResolveMarkerPosition();
        marker.transform.localScale = Vector3.one * PalmAnchorMarkerDiameter;
        SetSphereCollider(marker, true);
        SetVisibleMaterial(marker, new Color(1f, 0.92f, 0.12f, 1f));
        SetRenderersEnabled(marker, ShowDebugVisuals);

        GameObject sendZoneObject = GameObject.Find(SendZoneObjectName);
        if (sendZoneObject == null)
        {
            sendZoneObject = new GameObject(SendZoneObjectName);
            Undo.RegisterCreatedObjectUndo(sendZoneObject, "Create PalmHoverSendStartZone");
        }

        sendZoneObject.transform.position = marker.transform.position;

        PalmHoverSendStartZone sendZone = sendZoneObject.GetComponent<PalmHoverSendStartZone>();
        if (sendZone == null)
        {
            sendZone = Undo.AddComponent<PalmHoverSendStartZone>(sendZoneObject);
        }

        Undo.RecordObject(sendZone, "Configure PalmHoverSendStartZone");
        sendZone.sendZoneAnchor = marker.transform;
        sendZone.anchorVisual = marker;
        sendZone.sendZoneRadius = SendZoneRadius;
        sendZone.useAnchorAsSendZone = true;
        sendZone.showDebugVisuals = ShowDebugVisuals;
        sendZone.ResetRelativeAnchorCooldownSeconds = ResetRelativeAnchorCooldownSeconds;

        SelectObject selectObject = Object.FindObjectOfType<SelectObject>(true);
        if (selectObject != null)
        {
            sendZone.sendStartReceiver = selectObject;
            sendZone.sendStartMethodName = "StartPalmPosePublishing";
            Undo.RecordObject(selectObject, "Configure Absolute Calibration UI");
            selectObject.ConfigureAbsoluteScaledEeCalibration(true, true, ResetRelativeAnchorCooldownSeconds);
            EditorUtility.SetDirty(selectObject);
        }
        else
        {
            Debug.LogWarning("[ConfigureCalibrationFlowScene] SelectObject was not found. Assign sendStartReceiver manually.");
        }

        EditorUtility.SetDirty(marker);
        EditorUtility.SetDirty(sendZoneObject);
        EditorUtility.SetDirty(sendZone);
        MarkActiveSceneDirty();
        Debug.Log("[ConfigureCalibrationFlowScene] PalmAnchorMarker configured as SendZone anchor. radius="
            + SendZoneRadius.ToString("F2"));
    }

    private static GameObject GetOrCreateSphere(string objectName)
    {
        GameObject sphere = GameObject.Find(objectName);
        if (sphere != null)
        {
            return sphere;
        }

        sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = objectName;
        Undo.RegisterCreatedObjectUndo(sphere, "Create " + objectName);
        return sphere;
    }

    private static Vector3 ResolveMarkerPosition()
    {
        GameObject existingSendZoneSphere = GameObject.Find("SendZoneSphere");
        if (existingSendZoneSphere != null)
        {
            return existingSendZoneSphere.transform.position;
        }

        GameObject calibrationOperation = GameObject.Find("CalibrationOperation");
        if (calibrationOperation != null)
        {
            return calibrationOperation.transform.position;
        }

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 forwardFlat = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
            if (forwardFlat.sqrMagnitude < 0.0001f)
            {
                forwardFlat = camera.transform.forward;
            }

            return camera.transform.position + forwardFlat.normalized * 0.4f + Vector3.down * 0.22f;
        }

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null && sceneView.camera != null)
        {
            Transform sceneCamera = sceneView.camera.transform;
            return sceneCamera.position + sceneCamera.forward * 0.4f;
        }

        return new Vector3(0f, 1.2f, 0.4f);
    }

    private static void SetSphereCollider(GameObject sphere, bool isTrigger)
    {
        SphereCollider collider = sphere.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = sphere.AddComponent<SphereCollider>();
        }

        collider.isTrigger = isTrigger;
    }

    private static void SetVisibleMaterial(GameObject target, Color color)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = target.GetComponentInChildren<Renderer>(true);
        }

        if (renderer == null)
        {
            return;
        }

        Material material = renderer.sharedMaterial;
        if (material == null || material.name == "Default-Material")
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("[ConfigureCalibrationFlowScene] No compatible shader was found for PalmAnchorMarker.");
                return;
            }

            material = new Material(shader);
            material.name = "PalmAnchorMarker_Material";
            renderer.sharedMaterial = material;
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

    private static void SetRenderersEnabled(GameObject target, bool enabled)
    {
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = enabled;
        }
    }

    private static void MarkActiveSceneDirty()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(activeScene);
        }
    }

}
#endif
