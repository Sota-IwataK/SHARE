using UnityEngine;
using UnityEngine.Events;
using MixedReality.Toolkit.Input;
using MixedReality.Toolkit.UX;
using Microsoft.MixedReality.OpenXR;
using MixedReality.Toolkit.SpatialManipulation;
using UnityEngine.XR.Interaction.Toolkit;


public class BoundsControlHider : MonoBehaviour
{
    private ObjectManipulator manipulator;

    void Awake()
    {
        manipulator = GetComponent<ObjectManipulator>();

        if (manipulator != null)
        {
            manipulator.firstSelectEntered.AddListener(OnGrabStarted);
            manipulator.lastSelectExited.AddListener(OnGrabEnded);
           
        }
        else
        {
            Debug.LogWarning("ObjectManipulator ��������܂���I");
        }
    }

    private void OnGrabStarted(SelectEnterEventArgs args)
    {
        SetAllBoundsHandlesActive(false);
        //Debug.Log("�͂񂾂̂őSBoundsControl��Handles���\���ɂ��܂���");
    }

    private void OnGrabEnded(SelectExitEventArgs args)
    {
        SetAllBoundsHandlesActive(true);
        //Debug.Log("�������̂őSBoundsControl��Handles���ĕ\�����܂���");
    }

    private void SetAllBoundsHandlesActive(bool active)
    {
        // MRTK3�ł� BoundsControlRuntime ���g���Ă���\��������
        var boundsControls = FindObjectsOfType<BoundsControl>(true);
        foreach (var bounds in boundsControls)
        {
            bounds.HandlesActive = active;
            
        }
    }
}
