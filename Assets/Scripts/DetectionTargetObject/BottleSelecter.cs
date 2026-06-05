//using System.Collections;
//using System.Collections.Generic;
//using UnityEngine;
//using UnityEngine.Rendering;

//public class IRM_BottleSelector : MonoBehaviour
//{
//    // ROS Publisherへの参照（必要に応じてInspectorで設定）
//    [SerializeField] IRM_SerectObjectPublisher irmSelectPublisher;

//    // 選択されたボトルを格納するリスト
//    public List<GameObject> SelectedObjectList = new List<GameObject>();

//    // 座標計算の基準点（Unity上のArm Rootなど）
//    [SerializeField] private GameObject Origin;

//    // 選択時の色設定
//    [ColorUsage(false, true)] public Color SelectColor;

//    void Start()
//    {
//        // リストの初期化
//        if (SelectedObjectList == null)
//        {
//            SelectedObjectList = new List<GameObject>();
//        }
//    }

//    // ボトルに触れた時の処理（選択ロジック）
//    void OnTriggerEnter(Collider other)
//    {
//        // "bottle"タグのオブジェクトのみ対象
//        if (other.CompareTag("bottle"))
//        {
//            // まだリストに含まれていない場合のみ追加
//            if (!SelectedObjectList.Contains(other.gameObject))
//            {
//                // 1. 色を変更して選択状態を可視化
//                var rend = other.GetComponent<MeshRenderer>();
//                if (rend != null)
//                {
//                    rend.material.color = SelectColor;
//                    SetOpaque(rend.material); // マテリアルを不透明に設定
//                }

//                // 2. リストに追加
//                SelectedObjectList.Add(other.gameObject);
//            }
//        }
//    }

//    // Publisherスクリプトから呼び出される関数
//    // 選択されたボトルの座標リスト（ROS座標系）を返す
//    public float[] IRM_SelectMessage()
//    {
//        var selectCoords = new List<float>();

//        if (Origin == null)
//        {
//            Debug.LogError("Origin が設定されていません！Inspectorで設定してください。");
//            return selectCoords.ToArray();
//        }

//        // オフセット（元コードでは0,0,0）
//        Vector3 axisOffset = Vector3.zero;

//        // 基準点のワールド座標
//        Vector3 originWorld = Origin.transform.position;

//        foreach (GameObject obj in SelectedObjectList)
//        {
//            if (obj == null) continue;

//            // 1) ボトルのワールド座標
//            Vector3 bottleWorld = obj.transform.position;

//            // 2) 基準点からの相対位置を計算
//            Vector3 relative = bottleWorld - originWorld;

//            // 3) オフセットを加算
//            Vector3 adjusted = relative + axisOffset;

//            // 4) YouBot (ROS) 向けに軸反転・入れ替え
//            // Unity X -> ROS -X
//            // Unity Z -> ROS -Y
//            // Unity Y -> ROS Z
//            float youbot_x = -adjusted.x;
//            float youbot_y = -adjusted.z;
//            float youbot_z = adjusted.y;

//            // 5) 配列に追加
//            selectCoords.Add(youbot_x);
//            selectCoords.Add(youbot_y);
//            selectCoords.Add(youbot_z);
//        }

//        return selectCoords.ToArray();
//    }

//    // マテリアルをOpaque（不透明）モードに強制するヘルパー関数
//    private void SetOpaque(Material m)
//    {
//        m.SetFloat("_Surface", 0f);
//        m.SetOverrideTag("RenderType", "Opaque");
//        m.SetInt("_SrcBlend", (int)BlendMode.One);
//        m.SetInt("_DstBlend", (int)BlendMode.Zero);
//        m.SetInt("_ZWrite", 1);
//        m.DisableKeyword("_ALPHATEST_ON");
//        m.DisableKeyword("_ALPHABLEND_ON");
//        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
//        m.renderQueue = (int)RenderQueue.Geometry;
//    }
//}
