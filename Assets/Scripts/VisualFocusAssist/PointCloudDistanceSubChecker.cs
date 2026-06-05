using UnityEngine;

public class PointCloudDistanceSubChecker : MonoBehaviour
{
    public BottleSync bottleSync;
    public GameObject linePrefab;

    private LineRenderer lineRenderer;
    private PointCloudRenderer subPointCloudRenderer;

    void Start()
    {
        // LineRenderer を生成
        if (lineRenderer == null && linePrefab != null)
        {
            GameObject lineObj = Instantiate(linePrefab, transform); // 親は this にしてもOK
            lineRenderer = lineObj.GetComponent<LineRenderer>();
        }
    }

    void Update()
    {
        GameObject masterHit = bottleSync.GetCurrentHitObject(); // マスター側が検出したボトル
        if (masterHit == null) return;

        if (!bottleSync.TryGetSubFromMaster(masterHit, out GameObject subBottle)) return;
        if (subBottle == null) return;

        if (subPointCloudRenderer == null)
        {
            subPointCloudRenderer = bottleSync.GetSubPointCloudFromSubBottle(subBottle);
            if (subPointCloudRenderer == null) return;
        }

        Vector3[] points = subPointCloudRenderer.GetPointCloud();
        if (points == null || points.Length == 0) return;

        Vector3 subBottlePos = subBottle.transform.position;
        Vector3 closest = Vector3.zero;
        float minDist = float.MaxValue;

        foreach (var p in points)
        {
            Vector3 pWorld = subPointCloudRenderer.transform.TransformPoint(p);
            float d = (pWorld - subBottlePos).sqrMagnitude;
            if (d < minDist)
            {
                minDist = d;
                closest = pWorld;
            }
        }

        // ここがループ外に必要
        Vector3 worldClosest = closest;

        if (lineRenderer != null)
        {
            float dist = Mathf.Sqrt(minDist);
            if (dist <= 0.2f)
            {
                lineRenderer.enabled = true;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, subBottlePos);
                lineRenderer.SetPosition(1, worldClosest);

                Color gradColor = Color.Lerp(Color.white, Color.red, 1f - (dist / 0.2f));
                lineRenderer.startColor = gradColor;
                lineRenderer.endColor = gradColor;
            }
            else
            {
                lineRenderer.enabled = false;
            }
        }
    }

}
