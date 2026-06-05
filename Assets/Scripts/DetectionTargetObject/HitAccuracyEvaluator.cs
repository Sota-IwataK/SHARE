using UnityEngine;

public class HitAccuracyEvaluator : MonoBehaviour
{
    [Tooltip("ROSで予測されたIDを保持するサブスクライバ")]
    public DetectBottleSubscriber rosConnector;

    [Tooltip("エリア内／掴み判定を持つコンポーネント")]
    public BottleAreaChecker areaChecker;
    public bool isMatch = false;

    void Update()
    {
        // 1) ROS予測IDの取得（未受信なら-2）
        int rosId = -2;
        if (rosConnector != null && rosConnector.bottle_id >= 0)
            rosId = rosConnector.bottle_id;

        // 2) AreaCheckerから本当にユーザが掴んだボトル（IsHit==true）のIDを取得
        int actualHitId = -1;
        if (areaChecker != null)
        {
            foreach (var info in areaChecker.bottleInfos)
            {
                // 各Bottleの状態を見て、IsHit == true ならそれが掴み対象
                var state = info.bottle.GetComponent<BottleAreaState>();
                if (state != null && state.IsHit)
                {
                    actualHitId = info.id;
                    break;  // 複数ヒットする想定がなければ1つ見つけた時点でOK
                }
            }
        }
        if (actualHitId == rosId)
        {
            isMatch = true;
        }
        else
        {
            isMatch = false;
        }

        // 3) ログ出力
        // Debug.Log($"[HitLogger] ROS Predicted ID = {rosId}   Actual Hit ID = {actualHitId}");
    }
}
