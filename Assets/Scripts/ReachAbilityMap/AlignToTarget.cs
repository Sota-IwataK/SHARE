using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AlignToTarget : MonoBehaviour
{
    [Tooltip("���̃I�u�W�F�N�g�iA�j�̈ʒu�����킹�����Ώ� (B)")]
    public Transform targetTransform;

    void Update()
    {
        if (targetTransform != null)
        {
            // ���[���h���W���ۂ��ƃR�s�[

            transform.position = targetTransform.position;
            // Debug.Log($"{gameObject.name} target: {targetTransform.position}");
            // Debug.Log($"{gameObject.name} this  : {transform.position}");


            // �������������������Ȃ炱�����
            // transform.rotation = targetTransform.rotation;
        }
    }
}
