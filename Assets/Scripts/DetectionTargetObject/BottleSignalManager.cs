using System.Collections.Generic;
using UnityEngine;

public class BottleSignalManager : MonoBehaviour
{
    [Header("References")]
    public handTracking handTracking;
    public int     bottleID;    // ← 追加


    [Tooltip("頭部（カメラ）の Transform")]
    public Transform headTransform;
    [Tooltip("BottleAreaChecker の参照")]
    public BottleAreaChecker bottleAreaChecker;

    [Header("Proximity Settings")]
    [Tooltip("手先近接度を正規化する最大距離 R_max(m)")]
    public float handRMax = 0.5f;

    [Header("Computed Signals (ReadOnly)")]
    [Tooltip("各ボトルの生データ＋シグナル")]
    public List<BottleSignal> signals = new List<BottleSignal>();

    private Vector3 prevHandPos;
    private Vector3 prevHandVel;

    public GameObject origin_arm;

    void Start()
    {
         // 初期化
        if (handTracking != null)
        {
            prevHandPos = handTracking.middleObject.transform.position;
            prevHandVel = Vector3.zero;
        }
    }

    void Update()
    {
        if (handTracking == null || headTransform == null || bottleAreaChecker == null)
            return;

        

        // 手先速度・加速度
        Vector3 handPos = handTracking.middleObject.transform.position;
        Vector3 handVel = (handPos - prevHandPos) / Time.deltaTime;
        Vector3 rawAcc  = (handVel - prevHandVel) / Time.deltaTime;

        // 速度方向（単位ベクトル）
        Vector3 velDir = handVel.sqrMagnitude > 1e-6f
            ? handVel.normalized
            : Vector3.zero;

        // スカラー符号付き加速度
        float signedAcc = Vector3.Dot(rawAcc, velDir);

        // 正規化＆クランプ
        float maxAcc = 10f;  // 実際に想定される最大加速度を入れる
        float normAcc = Mathf.Clamp(signedAcc / maxAcc, -1f, 1f);

        // –1～1 のスカラーをベクトルに反映
        Vector3 handAccClamped = velDir * normAcc;

        // 次フレーム用に保存
        prevHandPos = handPos;
        prevHandVel = handVel;


        // 頭部位置・前方
        Vector3 headPos     = headTransform.position;
        Vector3 headForward = headTransform.forward;

        // 生データ取得
        var infos = bottleAreaChecker.bottleInfos;

        signals.Clear();
        foreach (var info in infos)
        {
            Vector3 p      = info.position;
            float   inside = info.isInside ? 1f : 0f;     // ← 0 or 1 に変換
            
            // 接度
            float s_touch = info.isHit ? 1f : 0f;

            // 手先近接度
            float dHand = Vector3.Distance(handPos, p);
            float s_hand = Mathf.Max(0f, 1f - (dHand / handRMax));

            // ヘッドアライメント
            Vector3 d_head = (p - headPos).normalized;
            float   s_head = Mathf.Max(0f, Vector3.Dot(headForward, d_head));

            // 加速度アライメント
            Vector3 d_hand  = (p - handPos).normalized;
            float   s_accel = Mathf.Max(0f, Vector3.Dot(velDir, d_hand));

            // arm根本からのボトル座標
            Vector3 originWorld = origin_arm.transform.position;
            Vector3 offset_p=p-originWorld;

            signals.Add(new BottleSignal
            {
                bottleID     = info.id,
                position   = offset_p,
                insideFlag = inside,
                s_touch    = s_touch,
                s_hand     = s_hand,
                s_head     = s_head,
                s_accel    = s_accel
            });
        }
    }

    [System.Serializable]
    public struct BottleSignal
    {
        public int      bottleID; 
        public Vector3    position;   // ボトル座標
        public float      insideFlag; // 0 or 1

        public float      s_touch;    // 接触度 0 or 1
        public float      s_hand;     // 手先近接度
        public float      s_head;     // ヘッドアライメント
        public float      s_accel;    // 加速度アライメント
    }
}
