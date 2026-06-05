using UnityEngine;

public class bottle : MonoBehaviour
{
    public GameObject bottlePrefab;  // プレハブ参照
    public float spawnDistance = 0.5f;  // 顔からの距離
    public GameObject parentObject;  // 生成されたオブジェクトの親オブジェクト（ヒエラルキー内）

    public void SpawnBottle()
    {
        if (bottlePrefab == null)
        {
            Debug.LogWarning("ボトルプレハブが設定されていません！");
            return;
        }

        // HMDのカメラ位置と向き
        Transform cameraTransform = Camera.main.transform;

        // 顔の前方に生成位置を計算
        Vector3 spawnPosition = cameraTransform.position + cameraTransform.forward * spawnDistance;

        // カメラのY軸回転のみ適用（水平）
        Quaternion horizontalRotation = Quaternion.Euler(0, cameraTransform.eulerAngles.y, 0);

        // 水平に生成
        GameObject spawnedBottle = Instantiate(bottlePrefab, spawnPosition, horizontalRotation);

        // ヒエラルキー内での親オブジェクトを設定
        if (parentObject != null)
        {
            spawnedBottle.transform.SetParent(parentObject.transform);
            //Debug.Log("生成されたオブジェクトをヒエラルキー内で親に設定しました。");
        }
        else
        {
            Debug.LogWarning("親オブジェクトが設定されていません！");
        }

        //Debug.Log("spown");
    }
}
