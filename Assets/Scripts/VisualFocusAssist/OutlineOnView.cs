
/*using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;
using System.Collections.Generic;
using UnityEngine;

using MixedReality.Toolkit.SpatialManipulation;
using MixedReality.Toolkit.Input;

public class OutlineOnView : MonoBehaviour
{
    public Camera playerCamera;
    [SerializeField] private float maxRaycastDistance = 0.75f;
    private RaycastHit hitObj;

    [SerializeField, Tooltip("掴んだときに表示するオブジェクト")]
    private GameObject targetUI;  // Inspector で表示対象のUIを指定
    public BottleSync bottleSync; // ← Inspector でアサイン


    public GameObject hitObject { get; private set; }  // 他スクリプトから取得可能に

    private GameObject GetCurrentlyGrabbedObject()
    {
        var manipulators = FindObjectsOfType<ObjectManipulator>();
        foreach (var manipulator in manipulators)
        {
            if (manipulator.interactorsSelecting != null && manipulator.interactorsSelecting.Count > 0)
                return manipulator.gameObject;
        }
        return null;
    }

    void Update()
    {
        GameObject grabbed = GetCurrentlyGrabbedObject();
        GameObject raycasted = null;

        if (Physics.Raycast(new Ray(playerCamera.transform.position, playerCamera.transform.forward), out hitObj, maxRaycastDistance))
        {
            raycasted = hitObj.collider.gameObject;
        }






        if (grabbed != null && grabbed.CompareTag("bottle"))
        {
            hitObject = grabbed;

            //  掴んでいるときは UI 表示
            if (targetUI != null && !targetUI.activeSelf)
            {
                targetUI.SetActive(true);
            }
        }
        else
        {
            if (targetUI != null && targetUI.activeSelf)
            {
                targetUI.SetActive(false);
            }

            if (raycasted != null && raycasted.CompareTag("bottle"))
            {
                hitObject = raycasted;
            }
            else
            {
                hitObject = null;
            }
        }

        if (hitObject != null && bottleSync != null)
        {
            bottleSync.SetCurrentHitObject(hitObject);
        }

    }
    public GameObject GetTargetUI()
    {
        return targetUI;
    }


}*/
/*
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;
using System.Collections.Generic;
using UnityEngine;
using System.Collections;
public class OutlineOnView : MonoBehaviour
{
    public Camera playerCamera;
    [SerializeField] private float maxRaycastDistance = 0.75f;
    private RaycastHit hitObj;
    [SerializeField] private GameObject targetUI;
    public BottleSync bottleSync;
    public GameObject ReachabilityFullObject; // InspectorでMeshColliderオブジェクトを指定

    public GameObject hitObject { get; private set; }
    private List<GameObject> outsideBottles = new List<GameObject>();

    private Collider reachabilityCollider;

    void Start()
    {
        if (ReachabilityFullObject != null)
            reachabilityCollider = ReachabilityFullObject.GetComponent<Collider>();

        StartCoroutine(UpdateOutsideBottlesLoop());
    }

    private bool IsInsideArea(GameObject obj)
    {
        if (reachabilityCollider == null || obj == null) return false;

        if (reachabilityCollider is SphereCollider sc)
        {
            Vector3 center = sc.transform.TransformPoint(sc.center); // ワールド座標に変換された球の中心
            float radius = sc.radius * sc.transform.lossyScale.x;    // ワールドスケールを考慮した半径

            float distance = Vector3.Distance(center, obj.transform.position);

            return distance <= radius;
        }

        return false;
    }




    private GameObject GetCurrentlyGrabbedObject()
    {
        var manipulators = FindObjectsOfType<ObjectManipulator>();
        foreach (var manipulator in manipulators)
        {
            if (manipulator.interactorsSelecting != null && manipulator.interactorsSelecting.Count > 0)
                return manipulator.gameObject;
        }
        return null;
    }

    void Update()
    {
        GameObject grabbed = GetCurrentlyGrabbedObject();
        GameObject raycasted = null;

        if (Physics.Raycast(new Ray(playerCamera.transform.position, playerCamera.transform.forward), out hitObj, maxRaycastDistance))
        {
            raycasted = hitObj.collider.gameObject;
        }

        hitObject = null; // ← 初期化
        

        // 優先順位1: 掴んでいて、タグがbottle、かつエリア内
        if (grabbed != null && grabbed.CompareTag("bottle") && IsInsideArea(grabbed))
        {
            hitObject = grabbed;
            //UnityEngine.Debug.Log("aria grab");
        }
        // 優先順位2: 掴んでいない場合にRaycast判定、タグbottleでエリア内
        else if (grabbed == null && raycasted != null && raycasted.CompareTag("bottle") && IsInsideArea(raycasted))
        {
            hitObject = raycasted;
            //UnityEngine.Debug.Log("aria ray");
        }
        
        // UI表示のON/OFF（hitObjectがnullかどうかで判定）
        if (targetUI != null)
        {
            targetUI.SetActive(hitObject != null);
        }

        // BottleSyncに共有
        if (hitObject != null && bottleSync != null)
        {
            bottleSync.SetCurrentHitObject(hitObject);
        }
    }

    public GameObject GetTargetUI() => targetUI;
    public List<GameObject> GetOutsideBottles()
    {
        return outsideBottles;
    }

    private IEnumerator UpdateOutsideBottlesLoop()
    {
        while (true)
        {
            UpdateOutsideBottles();
            yield return new WaitForSeconds(1f); // 1秒ごとに更新
        }
    }

    private void UpdateOutsideBottles()
    {
        outsideBottles.Clear();
        GameObject[] allBottles = GameObject.FindGameObjectsWithTag("bottle");

        foreach (var bottle in allBottles)
        {
            if (!IsInsideArea(bottle))
            {
                outsideBottles.Add(bottle);
            }
        }
    }

}
*/

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit.SpatialManipulation;

public class OutlineOnView : MonoBehaviour
{
    [Header("Raycast & Grab")]
    public Camera playerCamera;
    [SerializeField] private float maxRaycastDistance = 0.75f;
    private RaycastHit hitObj;

    [Header("UI & Sync")]
    [SerializeField] private GameObject targetUI;
    public BottleSync bottleSync;

    [Header("Area Check (MeshCollider)")]
    [Tooltip("Convex + IsTrigger 推奨")]
    public MeshCollider reachabilityMeshCollider;

    [Header("Outside List")]
    [Tooltip("タグ \"bottle\" のオブジェクトで範囲外のものを毎秒更新")]
    private List<GameObject> outsideBottles = new List<GameObject>();

    // 内部判定許容誤差
    const float eps = 1e-4f;
    // 1秒ごとに outsideBottles を更新
    const float outsideUpdateInterval = 1f;

    public GameObject hitObject { get; private set; }

    void Start()
    {
        // MeshCollider が無ければ同じオブジェクトから探す
        if (reachabilityMeshCollider == null)
            reachabilityMeshCollider = GetComponent<MeshCollider>();

        // Convex にしておくと挙動が安定します
        if (reachabilityMeshCollider != null)
            reachabilityMeshCollider.convex = true;

        StartCoroutine(UpdateOutsideBottlesLoop());
    }

    private IEnumerator UpdateOutsideBottlesLoop()
    {
        while (true)
        {
            UpdateOutsideBottles();
            yield return new WaitForSeconds(outsideUpdateInterval);
        }
    }

    private void UpdateOutsideBottles()
    {
        outsideBottles.Clear();
        foreach (var bottle in GameObject.FindGameObjectsWithTag("bottle"))
        {
            if (!IsInsideArea(bottle.transform.position))
            {
                outsideBottles.Add(bottle);
            }
        }
    }

    /// <summary>
    /// MeshCollider.ClosestPoint を使った内部判定
    /// 内部なら ClosestPoint==point を返します
    /// </summary>
    private bool IsInsideArea(Vector3 point)
    {
        if (reachabilityMeshCollider == null) return false;

        Vector3 closest = reachabilityMeshCollider.ClosestPoint(point);
        return (closest - point).sqrMagnitude < eps * eps;
    }

    void Update()
    {
        // ① Raycast／Grab の優先順位判定
        GameObject grabbed = null;
        foreach (var m in FindObjectsOfType<ObjectManipulator>())
        {
            if (m.interactorsSelecting?.Count > 0)
            {
                grabbed = m.gameObject;
                break;
            }
        }

        GameObject raycasted = null;
        if (Physics.Raycast(playerCamera.transform.position,
                            playerCamera.transform.forward,
                            out hitObj, maxRaycastDistance))
        {
            raycasted = hitObj.collider.gameObject;
        }

        // ② hitObject を決定
        hitObject = null;

        // 優先1: 掴んでいてボトルかつ内部
        if (grabbed != null && grabbed.CompareTag("bottle")
            && IsInsideArea(grabbed.transform.position))
        {
            hitObject = grabbed;
        }
        // 優先2: Raycastヒットだけど掴んでいない・ボトルかつ内部
        else if (grabbed == null
                 && raycasted != null
                 && raycasted.CompareTag("bottle")
                 && IsInsideArea(raycasted.transform.position))
        {
            hitObject = raycasted;
        }

        // ③ UI 表示切り替え
        if (targetUI != null)
            targetUI.SetActive(hitObject != null);

        // ④ BottleSync に共有
        if (hitObject != null && bottleSync != null)
            bottleSync.SetCurrentHitObject(hitObject);
    }

    /// <summary>
    /// 範囲外のボトルリストを外部参照用に取得
    /// </summary>
    public List<GameObject> GetOutsideBottles() => outsideBottles;
    public GameObject GetTargetUI() => targetUI;
}
