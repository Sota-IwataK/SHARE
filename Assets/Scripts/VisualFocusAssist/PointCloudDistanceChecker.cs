using UnityEngine;
using TMPro;

public class PointCloudDistanceChecker : MonoBehaviour
{
    public PointCloudRenderer pointCloudRenderer;
    public OutlineOnView outlineOnView;

    public TextMeshPro distanceText;

    public float checkInterval = 0.2f;
    private float timer = 0f;

    private int previousClosestIndex = -1; // ← 前回赤くしたインデックスを記録

    public GameObject linePrefab;  // ← Prefab をアサイン
    private LineRenderer lineRenderer;

    private Color currentGradColor = Color.white;
    public Color GetCurrentGradientColor() => currentGradColor;

    public GameObject glowSpherePrefab;
    private GameObject glowSphereInstance;

    void Start()
    {
        if (lineRenderer == null && linePrefab != null)
        {
            GameObject lineObj = Instantiate(linePrefab);
            lineRenderer = lineObj.GetComponent<LineRenderer>();
        }
    }

    void Update()
    {
        // インターバル制御
        timer += Time.deltaTime;
        if (timer < checkInterval) return;
        timer = 0f;

        // UI の表示状態をチェック
        var targetUI = outlineOnView?.GetTargetUI();
        if (targetUI == null || !targetUI.activeSelf)
        {
            if (lineRenderer != null) lineRenderer.enabled = false;
            if (distanceText != null) distanceText.text = "";
            ResetPreviousPointColor();
            return;
        }

        // ターゲットオブジェクト取得
        GameObject target = outlineOnView.hitObject;
        if (target == null) return;

        // ① 点群のローカル位置を取得
        Vector3[] localPoints = pointCloudRenderer?.GetPointCloud();
        if (localPoints == null || localPoints.Length == 0) return;

        // ② ターゲット位置をローカル座標に変換
        Vector3 localTarget = pointCloudRenderer.transform.InverseTransformPoint(target.transform.position);

        // ③ ローカル座標で最短点を探索
        float minDistSqr = float.MaxValue;
        int closestIndex = -1;
        for (int i = 0; i < localPoints.Length; i++)
        {
            float d2 = (localPoints[i] - localTarget).sqrMagnitude;
            if (d2 < minDistSqr)
            {
                minDistSqr = d2;
                closestIndex = i;
            }
        }
        if (closestIndex < 0) return;

        // ④ 距離とグラデーション色を計算
        float distance = Mathf.Sqrt(minDistSqr);
        float distanceCm = distance * 100f;
        float t = Mathf.Clamp01(1.0f - (distance / 0.20f));
        currentGradColor = Color.Lerp(Color.white, Color.red, t);

        // UI 更新
        if (distanceText != null)
        {
            distanceText.text = $"{distanceCm:F1} cm";
            distanceText.color = currentGradColor;
        }

        // ワールド座標に復元してライン描画
        Vector3 closestWorldPoint = pointCloudRenderer.transform.TransformPoint(localPoints[closestIndex]);
        if (lineRenderer != null)
        {
            if (distanceCm <= 20f)
            {
                lineRenderer.enabled = true;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, target.transform.position);
                lineRenderer.SetPosition(1, closestWorldPoint);
                lineRenderer.startColor = currentGradColor;
                lineRenderer.endColor = currentGradColor;
            }
            else
            {
                lineRenderer.enabled = false;
            }
        }

        // 点群カラー更新（ハイライト）
        var colors = pointCloudRenderer.GetColors();
        if (colors != null)
        {
            if (previousClosestIndex >= 0 && previousClosestIndex < colors.Length)
                colors[previousClosestIndex] = Color.white;
            colors[closestIndex] = currentGradColor;
            pointCloudRenderer.UpdateColors(colors);
            previousClosestIndex = closestIndex;
        }

        // Glow Sphere 表示
        if (distanceCm <= 20f)
        {
            if (glowSphereInstance == null && glowSpherePrefab != null)
            {
                glowSphereInstance = Instantiate(glowSpherePrefab);
            }
            if (glowSphereInstance != null)
            {
                glowSphereInstance.transform.position = closestWorldPoint;
                var glowRenderer = glowSphereInstance.GetComponent<Renderer>();
                if (glowRenderer != null)
                {
                    Material mat = glowRenderer.material;
                    Color emissionColor = currentGradColor * 0.5f;
                    mat.SetColor("_EmissionColor", emissionColor);
                }
            }
        }
        else if (glowSphereInstance != null)
        {
            Destroy(glowSphereInstance);
            glowSphereInstance = null;
        }
    }

    // UI 非表示時に前回ハイライトをリセット
    void ResetPreviousPointColor()
    {
        if (previousClosestIndex < 0) return;

        var colors = pointCloudRenderer.GetColors();
        if (colors != null && previousClosestIndex < colors.Length)
        {
            colors[previousClosestIndex] = Color.white;
            pointCloudRenderer.UpdateColors(colors);
        }

        previousClosestIndex = -1;
    }
}
