using UnityEngine;
using System.Diagnostics;

public class YoubotOffsetToggler : MonoBehaviour
{
    [Tooltip("�g�O�������� YoubotOffsetSubscriber �R���|�[�l���g")]
    [SerializeField] private YoubotOffsetSubscriber subscriber;
    // [SerializeField] private RM_follow_toggle RM_follow_toggle;




    /// <summary>
    /// �{�^���� OnClicked �փA�^�b�`
    /// </summary>
    public void ToggleSubscriber()
    {
        // RM_follow_toggle.ActiveTrue();
        subscriber.enabled = !subscriber.enabled;
        UnityEngine.Debug.Log($"YoubotOffsetSubscriber is now {(subscriber.enabled ? "Enabled" : "Disabled")}");

    }
}
