using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PositionRelativeToObject : MonoBehaviour
{
    public Transform targetObject; // 参照オブジェクト (配置の基準とするオブジェクト)
    public GameObject objectToMove; // 移動させるオブジェクト（非アクティブしても位置を取得）
    private Animator objectToMoveAnimator;
    public string parameterName = "AppearObject"; // Animatorのパラメータ名

    private bool isObjectVisible = false; // 現在の状態を保持

    public Vector3 positionOffset; // 位置のオフセット（インスペクタで調整可能）
    public Vector3 rotationOffset; // 回転のオフセット（インスペクタで調整可能）

    private Vector3 targetPosition; // オフセット適用後のターゲット位置を保存
    private Quaternion targetRotation; // オフセット適用後のターゲット回転を保存

    private void Start()
    {
        // 基準オブジェクトからの初期位置を取得
        if (targetObject != null)
        {
            UpdateObjectPosition();
        }

        if (objectToMove != null)
        {
            objectToMoveAnimator = objectToMove.GetComponent<Animator>();
        }
    }

    // ボタンがクリックされた時に呼び出されるメソッド
    public void PositionRelativeToTarget()
    {

        if (targetObject == null)
        {
            Debug.LogWarning("Target Objectが設定されていません！");
            return;
        }

        if (objectToMove == null)
        {
            Debug.LogWarning("Object to Moveが設定されていません！");
            return;
        }


        // 新しい位置と回転を更新する
        UpdateObjectPosition();

        objectToMove.transform.position = targetPosition;
        objectToMove.transform.rotation = targetRotation;

        // 現在の状態を反転
        isObjectVisible = !isObjectVisible;

        // Animatorのパラメータを更新
        objectToMoveAnimator.SetBool(parameterName, isObjectVisible);
    }

    private void UpdateObjectPosition()
    {
        // 参照オブジェクトの位置と回転を取得
        targetPosition = targetObject.position + targetObject.TransformDirection(positionOffset);
        targetRotation = targetObject.rotation * Quaternion.Euler(rotationOffset);

        // 高さをカメラと合わせるためにY座標をキープする（必要に応じて）
        targetPosition.y = targetObject.position.y;
    }
}
