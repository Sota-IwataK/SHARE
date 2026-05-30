
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;

public class ObjectHit : MonoBehaviour
{
    [Header("Raycast & Grab")]
    public Camera playerCamera;
    [SerializeField] private float maxRaycastDistance = 0.0f;
    public RaycastHit hitObj;

    public GameObject hitObject { get; private set; }//ここでhitを保持する

    void Update()
    {

        GameObject grabbed = null;
        foreach (var m in FindObjectsOfType<ObjectManipulator>())
        {
            if (m.interactorsSelecting?.Count > 0)
            {
                grabbed = m.gameObject;
                break;
            }
        }

        GameObject raycasted = null;
        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out hitObj, maxRaycastDistance))
        {
            raycasted = hitObj.collider.gameObject;
        }

        // �A hitObject ������
        hitObject = null;

        // �D��1: �͂�ł��ă{�g��������
        if (grabbed != null && grabbed.CompareTag("bottle")
            )
        {
            hitObject = grabbed;
        }
        // �D��2: Raycast�q�b�g�����ǒ͂�ł��Ȃ��E�{�g��������
        else if (grabbed == null
                 && raycasted != null
                 && raycasted.CompareTag("bottle")
                 )
        {
            hitObject = raycasted;
        }


    }

}
