using UnityEngine;

public class TargetUIController : MonoBehaviour
{
    [Header("スケール")]
    [Tooltip("生成直後のスケール倍率")]
    public float initialSize = 2f;

    [Tooltip("最終的に到達するスケール倍率")]
    public float targetSize = 1f;

    [Tooltip("初期→最終スケールを補間する時間（秒）")]
    public float shrinkDuration = 0.15f;

    [Header("回転")]
    [Tooltip("Z 軸回転速度 (度/秒)")]
    public float rotationSpeed = 45f;

    private float _elapsed;

    void Start()
    {
        // まずは大きいスケールで生成
        transform.localScale = Vector3.one * initialSize;
    }

    void Update()
    {
        // ── 1) スケール縮小 ──
        if (_elapsed < shrinkDuration)
        {
            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / shrinkDuration);
            // 線形補間。イージングさせるなら Mathf.SmoothStep(0,1,t) などに置き換え
            float s = Mathf.Lerp(initialSize, targetSize, t);
            transform.localScale = Vector3.one * s;
        }

        // ── 2) Z 軸回転 ──
        transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime, Space.Self);
    }
}
