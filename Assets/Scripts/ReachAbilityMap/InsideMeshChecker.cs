using UnityEngine;

public class InsideMeshChecker : MonoBehaviour
{
    public MeshCollider meshCollider;
    public Transform targetTransform;

    const float eps = 1e-4f;
    public float checkInterval = 1f;  // 1秒ごとにチェック
    float nextCheckTime = 0f;
    bool prevInside = false;

    void Reset()
    {
        if (meshCollider == null)
            meshCollider = GetComponent<MeshCollider>();
    }

    void Update()
    {
        if (Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;

        Vector3 closest = meshCollider.ClosestPoint(targetTransform.position);
        bool isInside = (closest - targetTransform.position).sqrMagnitude < eps * eps;

        // 状態変化時だけログ
        if (isInside != prevInside)
        {
            UnityEngine.Debug.Log($"対象 {(isInside ? "Inside" : "Outside")}");
            prevInside = isInside;
        }
    }
}
