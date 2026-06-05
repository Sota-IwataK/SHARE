using UnityEngine;

public class BottleHitMapper : MonoBehaviour
{
    [Tooltip("ROSから受け取ったボトルIDを保持するサブスクライバ")]
    public DetectBottleSubscriber rosconnector;

    [Tooltip("エリア判定＆bottleInfosを持つChecker")]
    public BottleAreaChecker areaChecker;

    // 今フレームヒット中のボトル参照
    public GameObject hitObject { get; private set; }

    void Update()
    {
        int id = rosconnector.bottle_id;

        // 1. 全ボトルをまずリセット
        hitObject = null;
        foreach (var info in areaChecker.bottleInfos)
        {
            var state = info.bottle.GetComponent<BottleAreaState>();
            if (state != null)
                state.SetHit(false);
        }

        // 2. ROS受信ID に該当するボトルだけ SetHit(true) & hitObject 設定
        if (id >= 0)
        {
            // LINQ でも書けますが、分かりやすくループで
            foreach (var info in areaChecker.bottleInfos)
            {
                if (info.id == id)
                {
                    var state = info.bottle.GetComponent<BottleAreaState>();
                    if (state != null)
                    {
                        state.SetHit(true);
                        hitObject = info.bottle;
                    }
                    break;
                }
            }
        }
    }
}
