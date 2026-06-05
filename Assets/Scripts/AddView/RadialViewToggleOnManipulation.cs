using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using MixedReality.Toolkit.SpatialManipulation;

public class RadialViewToggleOnManipulation : MonoBehaviour
{
    public XRBaseInteractable interactable;
    public RadialView radialView;

    private void Awake()
    {
        if (interactable == null)
            interactable = GetComponent<XRBaseInteractable>();

        if (interactable != null)
        {
            interactable.firstSelectEntered.AddListener(OnManipulationStarted);
            interactable.lastSelectExited.AddListener(OnManipulationEnded);
        }
    }

    private void OnManipulationStarted(SelectEnterEventArgs args)
    {
        if (radialView != null)
        {
            radialView.enabled = false;
            //Debug.Log("RadialView disabled during manipulation.");
        }
    }

    private void OnManipulationEnded(SelectExitEventArgs args)
    {
        if (radialView != null)
        {
            radialView.enabled = true;
            //Debug.Log("RadialView re-enabled after manipulation.");
        }
    }
}
