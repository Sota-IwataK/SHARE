using UnityEngine;

public class PinchSwipeUIController : MonoBehaviour
{
    [Header("Refs")]
    public airTap_distance2 pinchSource; // 親指＆人差し指の中点を提供
    public Animator animator;

    [Header("Animator Triggers")]
    public string enterTrigger = "Enter"; // 上→下
    public string hideTrigger = "Hide";   // 下→上

    [Header("Swipe Settings")]
    [Tooltip("上→下/下→上とみなす最小の縦移動量[m]")]
    public float swipeDistance = 0.10f;
    [Tooltip("スワイプ成立までの最大時間[s]")]
    public float maxSwipeDuration = 0.45f;
    [Tooltip("連続発火のクールダウン[s]")]
    public float cooldown = 0.35f;

    // 内部状態
    private bool pinchHeld = false;
    private float startY;
    private float startTime;
    private float lastFiredTime = -10f;

    void Update()
    {
        if (pinchSource == null || animator == null) return;

        bool isPinch = pinchSource.airtap; // 親指＆人差し指の距離が近いか
        float currentY = pinchSource.middlePoint.y; // 中点の高さ


        // ピンチ開始
        if (!pinchHeld && isPinch==true)
        {
            pinchHeld = true;
            startY = currentY;
            startTime = Time.time;
            return;
        }

        // ピンチ解除
        if (pinchHeld && !isPinch==true)
        {
            pinchHeld = false;
            return;
        }

        // ピンチ中にスワイプ判定
        if (pinchHeld && (Time.time - lastFiredTime) > cooldown)
        {
            float dt = Time.time - startTime;
            if (dt <= maxSwipeDuration)
            {
                float deltaY = currentY - startY; // 上昇(+)、下降(-)

                // ↓ Enter（上→下）
                if (deltaY <= -swipeDistance)
                {
                    animator.SetBool("ShowUI", true);
                    Debug.Log("Enter (↓ Swipe)");
                    lastFiredTime = Time.time;
                    pinchHeld = false;
                }
                // ↓ Hide（下→上）
                else if (deltaY >= swipeDistance)
                {
                    animator.SetBool("ShowUI", false);
                    Debug.Log("Hide (↑ Swipe)");
                    lastFiredTime = Time.time;
                    pinchHeld = false;
                }
            }
            else
            {
                // 長すぎるとリセット
                pinchHeld = false;
            }
        }
    }
}
