using UnityEngine;
using UnityEngine.UI;

public class GameUI1 : MonoBehaviour
{
    [SerializeField] private RectTransform FxHolder;
    [SerializeField] private Image Circle_image;

    // ★ ShowDetectScore を参照（同一ボトルのものを割り当て）
    [SerializeField] private ShowDetectScore scoreUI;

    // 見た目をなめらかにしたい場合（任意）
    [SerializeField] private bool smooth = true;
    [SerializeField] private float smoothSpeed = 8f;

    // Inspector で見たいので残すけど、更新はコード側で行う
    [SerializeField, Range(0,1)] private float progress = 0f;

    void Awake()
    {
        if (scoreUI == null)
            scoreUI = GetComponentInParent<ShowDetectScore>(); // 同じ階層なら自動取得
    }

    private void Update()
    {
        if (Circle_image == null || scoreUI == null) return;

        float target = scoreUI.NormalizedScore;

        // 0..1 の進捗に反映
        progress = smooth
                   ? Mathf.Lerp(progress, target, Time.deltaTime * smoothSpeed)
                   : target;

        Circle_image.fillAmount = progress;

        // ★ 色も ShowDetectScore と同じにする
        Circle_image.color = scoreUI.CurrentColor;

        // 針・エフェクトの回転（必要に応じて調整）
        FxHolder.rotation = Quaternion.Euler(0f, 0f, -progress * 180f);
    }
}
