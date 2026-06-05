using UnityEngine;
using TMPro;

public class ShowDetectScore : MonoBehaviour
{
    [Header("References")]
    public TMP_Text text;
    private DetectBottle_score_subscriber scoreSubscriber;

    [Header("Display")]
    public bool showUnits = false;
    public string units = "";
    public string numberFormat = "F3";

    [Header("Color (optional)")]
    public Color staticColor = Color.white;
    public bool useGradient = false;
    public Gradient gradient;
    public float minScore = 0f, maxScore = 1f;

    //  公開プロパティ（GameUI1 から読む）
    public float RawScore { get; private set; } = float.NaN;
    public float NormalizedScore { get; private set; } = 0f; // 0..1
    public Color CurrentColor { get; private set; } = Color.white;

    void Awake()
    {
        if (scoreSubscriber == null)
            scoreSubscriber = FindObjectOfType<DetectBottle_score_subscriber>();

        if (text == null)
            text = GetComponent<TMP_Text>();
    }

    void Update()
    {
        if (scoreSubscriber == null) return;

        float score = scoreSubscriber.bottle_score;
        if (float.IsNaN(score)) return;

        RawScore = score;

        // 0..1 に正規化（min==max でも InverseLerp は0を返す）
        float t = Mathf.InverseLerp(minScore, maxScore, score);
        NormalizedScore = Mathf.Clamp01(t);

        // 色を決定して保持
        Color c = (useGradient && gradient != null)
                    ? gradient.Evaluate(NormalizedScore)
                    : staticColor;
        CurrentColor = c;

        // テキスト表示
        if (text != null)
        {
            text.text = showUnits && !string.IsNullOrEmpty(units)
                        ? $"{score.ToString(numberFormat)} {units}"
                        : score.ToString(numberFormat);
            text.color = c;
        }
    }
}
