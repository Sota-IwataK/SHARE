using MixedReality.Toolkit.UX;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;

public class DisableCollider: MonoBehaviour
{
    private Collider[] colliders;

    void Start()
    {
        colliders = GetComponents<Collider>();
        var manipulator = GetComponent<ObjectManipulator>();

        if (manipulator != null && colliders.Length > 0)
        {
            manipulator.firstSelectEntered.AddListener(_ => SetCollidersEnabled(false));
            manipulator.lastSelectExited.AddListener(_ => SetCollidersEnabled(true));
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        foreach (var col in colliders)
        {
            col.enabled = enabled;
        }
    }
}
