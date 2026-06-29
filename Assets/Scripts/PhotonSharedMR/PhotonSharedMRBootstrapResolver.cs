using UnityEngine;

public static class PhotonSharedMRBootstrapResolver
{
    public static PhotonFusionSharedRoomBootstrap EnsureBootstrap(
        ref PhotonFusionSharedRoomBootstrap bootstrap,
        Object owner,
        string method,
        bool logIfMissing = false)
    {
        bool needsResolve = bootstrap == null || !bootstrap.gameObject.activeInHierarchy;
        if (!needsResolve)
        {
            return bootstrap;
        }

        PhotonFusionSharedRoomBootstrap previous = bootstrap;
        bootstrap = Object.FindFirstObjectByType<PhotonFusionSharedRoomBootstrap>(FindObjectsInactive.Include);
        string ownerName = owner != null ? owner.GetType().Name : "UnknownOwner";
        string methodName = string.IsNullOrWhiteSpace(method) ? "UnknownMethod" : method;

        if (bootstrap != null)
        {
            if (previous != bootstrap)
            {
                Debug.Log("[PhotonSharedMRBootstrapResolver] PHOTON_BOOTSTRAP_REACQUIRED"
                    + " owner=" + ownerName
                    + " method=" + methodName
                    + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress
                    + " bootstrapActive=" + bootstrap.gameObject.activeInHierarchy
                    + " bootstrapHierarchy=" + FormatPath(bootstrap.transform));
            }

            return bootstrap;
        }

        if (logIfMissing)
        {
            Debug.LogError("[PhotonSharedMRBootstrapResolver] PHOTON_BOOTSTRAP_MISSING"
                + " owner=" + ownerName
                + " method=" + methodName
                + " calibrationInProgress=" + PhotonSharedMRCalibrationGuard.CalibrationInProgress);
        }

        return null;
    }

    public static string FormatPath(Transform transform)
    {
        if (transform == null)
        {
            return "None";
        }

        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}
