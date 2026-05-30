
using UnityEngine;
using System.Collections.Generic; 
public class BottleAreaChecker : MonoBehaviour
{
    [Header("Area Collider")]
    [Tooltip("Convex MeshCollider defining the reachable area.")]
    public MeshCollider areaCollider;
    public ObjectHit ObjectHit;//hitを保持するためだけのスクリプト

    [Header("Threshold (meters)")]
    [Tooltip("Distance tolerance for inside detection.")]
    public float insideEps = 0.01f;

        [System.Serializable]
    public struct BottleAreaInfo
    {
        public GameObject bottle;
        public int      id;
        public Vector3 position;
        public bool       isInside;
        public bool       isHit;
    }
    [Tooltip("各ボトルの位置と inside/hit 状態")]
    public List<BottleAreaInfo> bottleInfos = new List<BottleAreaInfo>();




    // 内部で使う二乗誤差
    private float insideEpsSq;

    void Awake()
    {
        areaCollider = areaCollider ?? GetComponent<MeshCollider>();
        if (!areaCollider.convex) areaCollider.convex = true;
        insideEpsSq = insideEps * insideEps;
    }

    void Update()
{
    // ① ヒット中のボトルを取得
    GameObject hitBottle = ObjectHit != null ? ObjectHit.hitObject : null;

    bottleInfos.Clear();

    // タグ "bottle" を毎フレーム検索
    var allBottles = GameObject.FindGameObjectsWithTag("bottle");
    for (int i = 0; i < allBottles.Length; i++)
    {
        // ← ここで変数に代入
        GameObject bottle = allBottles[i];

        // 座標取得
        Vector3 pos = bottle.transform.position;
        // inside/outside 判定
        Vector3 closest = areaCollider.ClosestPoint(pos);
        bool inside = (closest - pos).sqrMagnitude <= insideEpsSq;
        // hit 判定
        bool hit = (bottle == hitBottle);

        // 各ボトル側にも伝搬
        var state = bottle.GetComponent<BottleAreaState>();
        if (state != null)
        {
            state.SetInside(inside);
            state.SetHit(hit);
        }

            // List に追加
            bottleInfos.Add(new BottleAreaInfo
            {
                bottle = bottle,
                id = i,
                position = pos,
                isInside = inside,
                isHit = hit
            });
    }
}

}
