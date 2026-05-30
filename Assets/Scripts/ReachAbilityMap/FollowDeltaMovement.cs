using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FollowDeltaMovement : MonoBehaviour
{
     [Tooltip("動きをコピーしたい対象")]
    public Transform target;

    // 前フレームのターゲットの状態を保存
    private Vector3    _prevPos;
    private Quaternion _prevRot;

    void Start()
    {
        if (target == null)
        {
            Debug.LogError("FollowDeltaMovement: target がセットされていません！");
            enabled = false;
            return;
        }

        _prevPos = target.position;
        _prevRot = target.rotation;
    }

    void LateUpdate()
    {
        // 位置差分を計算して自分に適用
        Vector3 deltaPos = target.position - _prevPos;
        transform.position += deltaPos;

        // 回転差分を計算して自分に適用（必要なければコメントアウト）
        Quaternion deltaRot = target.rotation * Quaternion.Inverse(_prevRot);
        transform.rotation = deltaRot * transform.rotation;

        // 次フレーム用に保存
        _prevPos = target.position;
        _prevRot = target.rotation;
    }
}
