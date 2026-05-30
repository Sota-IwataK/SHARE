using System.Diagnostics;
using UnityEngine;

/// <summary>
/// YoubotOffsetSubscriber �� BaseMovePosition ���g����
/// �w�肵���I�u�W�F�N�g�������z�u����̑��Έړ��Ƃ��ē������R���|�[�l���g
/// </summary>
public class BasePositionMover : MonoBehaviour
{
    [Tooltip("BaseMovePosition ���v�Z���Ă��� Subscriber")]
    [SerializeField] private YoubotOffsetSubscriber offsetSubscriber;

    [Tooltip("�ړ��Ώۂ� Transform")]
    [SerializeField] private Transform targetTransform;
    [SerializeField] private Transform targetTransform1;

    // �V�[����̍ŏ��̃��[�J���ʒu���L��
    private Vector3 initialLocalPos;
    private Vector3 initialLocalPos1;

    void Start()
    {
        if (offsetSubscriber == null || targetTransform == null)
        {
            UnityEngine.Debug.LogError("OffsetSubscriber �܂��� TargetTransform ���ݒ肳��Ă��܂���B");
            enabled = false;
            return;
        }
        // �ŏ��̃��[�J���ʒu���L���v�`��
        initialLocalPos = targetTransform.localPosition;
        initialLocalPos1 = targetTransform1.localPosition;
    }

    void Update()
    {
        // Subscriber �Ōv�Z���ꂽ BaseMovePosition ��
        // �u���_(0,0,0) ����̐�Έʒu�v�ł͂Ȃ��u�ŏ��̃I�t�Z�b�g����̑��Έʒu�v
        // �ɂȂ��Ă���z��ł��B������΍��W�Ȃ� position ���g���Ă��������B

        // �����ł� initialLocalPos �ɑ��΃I�t�Z�b�g�𑫂��Ă��܂��B
        Vector3 relativeOffset = offsetSubscriber.BaseMovePosition;
        targetTransform.localPosition = initialLocalPos + relativeOffset;
        targetTransform1.localPosition = initialLocalPos1 + relativeOffset;
    }
}
