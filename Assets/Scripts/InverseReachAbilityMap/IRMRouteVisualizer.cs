using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;
using RosSharp.RosBridgeClient;
using RosSharp.RosBridgeClient.MessageTypes.Nav;
using static MixedReality.Toolkit.SpatialManipulation.ObjectManipulator;
using MixedReality.Toolkit.Input;
using RosSharp.Urdf;


public class IRMRouteVisual : MonoBehaviour
{
    [Header("References")]
    public IRMRouteSubscriber    subscriber;    // 経路購読
    public IRMRoutePathPublisher publisher;     // 経路再配信
    public Transform waypointsContainer;  // ← 追加
    public GameObject Aligin;

    [Header("Prefabs & Transforms")]
    [Tooltip("編集可能ウェイポイント用プレハブ(ObjectManipulator付き)")]
    public GameObject waypointPrefab;
    [Tooltip("LineRenderer付きプレハブ")]
    public GameObject lineRendererPrefab;
    [Tooltip("経路のスタート地点にしたいオブジェクトのTransform")]
    public Transform startTransform;

    [Tooltip("初回表示用案内メッセージ(3D Textなど)")]
    public GameObject suggestionMessage;

    [Header("Display Settings")]
    [Tooltip("案内メッセージ表示位置(カメラ前の距離)")]
    public float messageDistance = 2.0f;

    private List<GameObject> waypoints     = new List<GameObject>();
    private LineRenderer     lineRenderer;
    private bool             hasVisualized = false;
   

    void Start()
    {
        subscriber = subscriber ?? GetComponent<IRMRouteSubscriber>();
        publisher  = publisher  ?? GetComponent<IRMRoutePathPublisher>();

 

        // LineRenderer を Waypoints コンテナの下に生成
        // (1) parent だけ指定してインスタンス化
        var lrObj = Instantiate(lineRendererPrefab, waypointsContainer);

        // (2) 親基準のローカル原点にリセット
        lrObj.transform.localPosition = Vector3.zero;
        lrObj.transform.localRotation = Quaternion.identity;

        // (3) LineRenderer コンポーネント取得＋初期化
        lineRenderer = lrObj.GetComponent<LineRenderer>();
        lineRenderer.positionCount = 0;

        // Publisher 初期化
        publisher.WayPointObjectList = new List<GameObject>();
    }

    void Update()
    {
        if (subscriber.messagePath != null && subscriber.isDirty)
        {
            
            Visualize(subscriber.messagePath);
            ShowSuggestionMessage();
            
            hasVisualized = true;
            subscriber.isDirty = false;
        }
    }

    public void DisvisualWaypoint()
    {
        // 1) ウェイポイント群
        if (waypointPrefab)
            waypointPrefab.SetActive(false);   // 子もまとめて非アクティブ

        // 2) ライン描画を消す
        if (lineRendererPrefab)
        {
            lineRendererPrefab.SetActive(false);
        }
    }
    

    private void Visualize(Path path)
    {
        Clear();  // マーカーも publisher.WayPointObjectList もクリア済み
        

        // (A) 頂点数を start＋waypoints 数に合わせる
        int n = path.poses.Length;
        lineRenderer.positionCount = n + 1;

        // (B) １点目は startTransform
        lineRenderer.SetPosition(0, startTransform.position);


        for (int i = 0; i < path.poses.Length; i++)
    {
        var ps = path.poses[i];


            // ROS→Unity 座標変換（地面XZのみ）
            Vector3 rosPt = new Vector3(
            -(float)ps.pose.position.x,
            0f,
            -(float)ps.pose.position.y
                        );


            // (1) ROS→Unity のローカル座標に変換（地面XZのみ）
            Vector3 rosLocal = new Vector3(
                -(float)ps.pose.position.x,  // ROS の x → Unity の -X
                0f,                          // 地面に固定
                -(float)ps.pose.position.y   // ROS の y → Unity の -Z
            );

            // (2) 親だけ指定してインスタンス化
            var go = Instantiate(waypointPrefab, waypointsContainer);

            // (3) ローカル座標・回転を設定
            go.transform.localPosition = rosLocal;
            go.transform.localRotation = Quaternion.identity;

            waypoints.Add(go);

        // ObjectManipulator 設定
        var manip = go.GetComponent<ObjectManipulator>() 
                 ?? go.AddComponent<ObjectManipulator>();
        int idx = i; // クロージャ対策
        manip.lastSelectExited.AddListener(_ => OnWaypointMoved(go, idx));

            // (D) そのワールド位置を頂点 i+1 にセット
            Vector3 worldPos = go.transform.position;
            lineRenderer.SetPosition(i + 1, worldPos);


            // Publisher 登録
            publisher.WayPointObjectList.Add(go);
    }
    
        
}


    private void OnWaypointMoved(GameObject movedWaypoint, int index)
    {
        // index は Waypoint のループインデックス → Line 上では index+1
        int lineIdx = index + 1;
        if (lineIdx < lineRenderer.positionCount)
            lineRenderer.SetPosition(lineIdx, movedWaypoint.transform.position);
    }

    private void Clear()
    {
        foreach (var wp in waypoints) Destroy(wp);
        waypoints.Clear();
        lineRenderer.positionCount = 0;
        publisher.WayPointObjectList.Clear();
    }

    private void ShowSuggestionMessage()
    {
        if (suggestionMessage == null || suggestionMessage.activeSelf) return;
        var cam = Camera.main;
        if (cam == null) return;

        suggestionMessage.transform.position = cam.transform.position + cam.transform.forward * messageDistance;
        suggestionMessage.transform.rotation = Quaternion.LookRotation(cam.transform.forward);
        suggestionMessage.SetActive(true);
    }

    /// <summary>
    /// UIボタンなどから呼び出し、編集経路をROSへ送信
    /// </summary>
    public void PublishEditedRoute()
    {
        
        Aligin.GetComponent<AlignToTarget>().enabled = false;
        publisher.PublishStatus = true;

    }
}
