using UnityEngine;

[DisallowMultipleComponent]
public class PhotonSharedMRRootLifetimeLogger : MonoBehaviour
{
    private void OnEnable()
    {
        PhotonSharedMRCalibrationGuard.LogPhotonRootEnabled(transform);
    }

    private void Update()
    {
        PhotonSharedMRCalibrationGuard.TickCalibrationFrame();
    }

    private void OnDisable()
    {
        PhotonSharedMRCalibrationGuard.LogPhotonRootDisabled(transform);
    }

    private void OnDestroy()
    {
        PhotonSharedMRCalibrationGuard.LogPhotonRootDestroyed(transform);
    }
}
