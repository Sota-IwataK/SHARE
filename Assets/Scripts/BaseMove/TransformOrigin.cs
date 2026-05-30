using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TransformOrigin : MonoBehaviour
{
    [Header("�g���b�L���O�Q��")]
    [SerializeField] private Transform head;        // ���[�U�[�̓���i��FCamera.main.transform�j

    [Header("�L�����u���[�V�����Ώ�")]
    [SerializeField] private Transform origin;      // �ʒu�E��]�𓮂�����ʃI�u�W�F�N�g
    [SerializeField] private Transform boundingBox; // ���ԃm�[�h
    [SerializeField] private Transform child;       // ���ۂɉ��ʂœ������Ă���I�u�W�F�N�g
    // [SerializeField] private RM_follow_toggle RM_follow_toggle;

    // Start ���ɋL�^���Ă����u�����̃��[�J���I�t�Z�b�g�v
    private Vector3 defaultOriginLocal;
    private Vector3 defaultBoundingLocal;
    private Vector3 defaultChildLocal;
    private Vector3 defaultOffsetTotal;  // head ���N�_�Ƃ������� child �̃��[���h�I�t�Z�b�g

    void Start()
    {
        // Hierarchy ��̊e���[�J���l���L���v�`��
        defaultOriginLocal = origin.localPosition;
        defaultBoundingLocal = boundingBox.localPosition;
        defaultChildLocal = child.localPosition;

        // head ���[�J����Ԃł̃I�t�Z�b�g���a���v�Z
        defaultOffsetTotal = defaultOriginLocal
                           + defaultBoundingLocal
                           + defaultChildLocal;
    }

    /// <summary>
    /// �C�Ӄ^�C�~���O�ŌĂԂƁA
    /// �u���� child ���ǂ��ɂ����Ă��v�A
    /// child �̃��[�J�� Transform ��ς�����
    /// origin �̂ݓ������� child �������ʒu�ɍ��킹����
    /// </summary>
    public void CalibrateNow()
    {

        // �P�jboundingBox��child �̌��݃��[�J������ Transform ���擾
        Vector3 combinedLocalPos = boundingBox.localPosition
                                + boundingBox.localRotation * child.localPosition;
        Quaternion combinedLocalRot = boundingBox.localRotation * child.localRotation;

        // �Q�j������Ƃ����u���� child �̃��[���h�ʒu�v
        Vector3 desiredChildWorldPos = head.TransformPoint(defaultOffsetTotal);

        // �R�j���� Y ����]�i���[�j�݂̂����o��
        float headYaw = head.eulerAngles.y;
        Quaternion headYawOnly = Quaternion.Euler(0f, headYaw, 0f);

        // �S�jdesired �̉�]�̓s�b�`�^���[���O�Ń��[�̂ݔ��f
        Quaternion desiredChildWorldRot = headYawOnly;

        // �T�jOrigin �� world ��]���t�Z
        //    origin.worldRot * combinedLocalRot = desiredChildWorldRot
        Quaternion newOriginWorldRot = desiredChildWorldRot * Quaternion.Inverse(combinedLocalRot);

        // �U�jOrigin �� world �ʒu���t�Z
        //    origin.worldPos + newOriginWorldRot * combinedLocalPos = desiredChildWorldPos
        Vector3 newOriginWorldPos = desiredChildWorldPos
                                 - newOriginWorldRot * combinedLocalPos;

        // �V�jOrigin �Ɉꔭ�Z�b�g
        origin.SetPositionAndRotation(newOriginWorldPos, newOriginWorldRot);
    }
}
