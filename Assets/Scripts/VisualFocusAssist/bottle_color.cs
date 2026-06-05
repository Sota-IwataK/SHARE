using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class bottle_color : MonoBehaviour
{
    public OutlineOnView _rayManager;
    public BottleSync bottleSync;  // Inspectorで設定

    [ColorUsage(false, true)] public Color PickColor;       // 選択中
    [ColorUsage(false, true)] public Color OtherColor;      // 非選択（透明度あり）
    [ColorUsage(false, true)] public Color OriginalColor;   // 選択なし

    public float fadeSpeed = 15f;            // フェード速度（大きいほど速い）
    public GameObject targetUIPrefab;
    private GameObject currentTargetBottle = null;
    private GameObject currentTargetUI = null;

    private Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    private Dictionary<GameObject, Material> materials = new Dictionary<GameObject, Material>();
    private Dictionary<GameObject, List<GameObject>> masterToSubMapping = new Dictionary<GameObject, List<GameObject>>();


    private GameObject[] bottles;
    // 透明かどうかを記録しておいて、変わった時だけ切り替える
    private Dictionary<GameObject, bool> isTransparent = new Dictionary<GameObject, bool>();

    void Start()
    {
        bottles = GameObject.FindGameObjectsWithTag("bottle");

        // BottleSyncからマッピングを受け取る
        var syncMapping = bottleSync.GetMasterToSubMapping();
        foreach (var entry in syncMapping)
        {
            GameObject master = entry.Key;
            GameObject sub = entry.Value;

            // master
            var masterRenderer = master.GetComponent<Renderer>();
            if (masterRenderer != null)
            {
                Material mat = new Material(masterRenderer.material); // 個別化
                SetMaterialTransparent(mat);
                masterRenderer.material = mat;

                materials[master] = mat;
                originalColors[master] = mat.GetColor("_BaseColor");
                isTransparent[master] = false;
            }

            // sub
            var subRenderer = sub.GetComponent<Renderer>();
            if (subRenderer != null)
            {
                Material subMat = new Material(subRenderer.material);
                SetMaterialTransparent(subMat);
                subRenderer.material = subMat;
            }

            if (!masterToSubMapping.ContainsKey(master))
                masterToSubMapping[master] = new List<GameObject>();

            masterToSubMapping[master].Add(sub);
        }
    }





    void Update()
    {
        GameObject hit = _rayManager.hitObject;
        if (hit != currentTargetBottle)
        {
            // 古いカーソルを消す
            if (currentTargetUI != null)
            {
                Destroy(currentTargetUI);
                currentTargetUI = null;
            }

            currentTargetBottle = hit;

            // 新しいターゲットがあれば UI を生成
            if (currentTargetBottle != null && targetUIPrefab != null)
            {
                // ボトルのワールド位置
                Vector3 bottlePos = currentTargetBottle.transform.position;
                // カメラ位置
                Transform camT = Camera.main.transform;

                // カメラ方向ベクトル（ボトル→カメラ）
                Vector3 dirToCam = (camT.position - bottlePos).normalized;

                // 前方オフセット距離（お好みで調整）
                float forwardOffset = 0.1f;  // 10cm 手前に
                                          
                float heightOffset = 0.1f;   // 10cm 上に
                                             // 最終的な UI 配置位置
                Vector3 uiPos = bottlePos
                              + dirToCam * forwardOffset
                              + Vector3.up * heightOffset;

                // カメラ常向き回転は変わらず
                Vector3 toCamFlat = camT.position - uiPos;
                toCamFlat.y = 0;
                Quaternion uiRot = Quaternion.LookRotation(toCamFlat.normalized, Vector3.up);

                currentTargetUI = Instantiate(
                    targetUIPrefab,
                    uiPos,
                    uiRot,
                    currentTargetBottle.transform
                );
            }
        }
        List<GameObject> outsideBottles = _rayManager.GetOutsideBottles();
        // マスター-サブの対応リストが有効なボトルのみ対象
        foreach (var bottle in bottles)
        {
            
            bool isOutside = outsideBottles.Contains(bottle);

            if (!materials.ContainsKey(bottle)) continue;

            // --- 色を決定 ---
            Color targetColor;
            bool shouldBeTransparent;


            if (isOutside)
            {
                // エリア外は常にOtherColor + 透明
                targetColor = OtherColor;
                shouldBeTransparent = true;
            }
            else if (hit == bottle)
            {
                // エリア内で選択中
                targetColor = PickColor;
                shouldBeTransparent = false;
            }
            else if (hit == null)
            {
                // エリア内で何も選択されていない（未選択）
                targetColor = originalColors[bottle];
                shouldBeTransparent = false;
            }
            else
            {
                // エリア内だが他のボトルが選択中
                targetColor = OtherColor;
                shouldBeTransparent = true;
            }



            // --- マスターの色を更新 ---
            Material mat = materials[bottle];
            if (!isTransparent.ContainsKey(bottle) || isTransparent[bottle] != shouldBeTransparent)
            {
                if (shouldBeTransparent)
                    SetMaterialTransparent(mat);
                else
                    SetMaterialOpaque(mat);

                isTransparent[bottle] = shouldBeTransparent;
            }

            Color currentColor = mat.GetColor("_BaseColor");
            Color newColor = Color.Lerp(currentColor, targetColor, Time.deltaTime * fadeSpeed);
            mat.SetColor("_BaseColor", newColor);

            // --- 対応するサブボトルにのみ色反映 ---
            if (masterToSubMapping.TryGetValue(bottle, out List<GameObject> subList))
            {
                foreach (var subBottle in subList)
                {
                    if (subBottle == null) continue;
                    var subRenderer = subBottle.GetComponent<Renderer>();
                    if (subRenderer != null)
                    {
                        subRenderer.material.color = newColor;

                        if (shouldBeTransparent)
                            SetMaterialTransparent(subRenderer.material);
                        else
                            SetMaterialOpaque(subRenderer.material);
                    }
                }
            }
        }
    }




    // マテリアルを透明に対応させる
    void SetMaterialTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void SetMaterialOpaque(Material mat)
    {
        mat.SetFloat("_Surface", 0); // Opaque
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

}

/*
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class bottle_color : MonoBehaviour
{
    public OutlineOnView _rayManager;
    public BottleSync bottleSync;  // Inspectorで設定
    public GameObject ReachabilityFullObject; // ← InspectorでMeshColliderをセット

    [ColorUsage(false, true)] public Color PickColor;       // 選択中
    [ColorUsage(false, true)] public Color OtherColor;      // 非選択（透明度あり）
    [ColorUsage(false, true)] public Color OriginalColor;   // 選択なし

    public float fadeSpeed = 15f;            // フェード速度（大きいほど速い）

    private Collider reachabilityCollider;
    private Dictionary<GameObject, Color> originalColors = new Dictionary<GameObject, Color>();
    private Dictionary<GameObject, Material> materials = new Dictionary<GameObject, Material>();
    private Dictionary<GameObject, List<GameObject>> masterToSubMapping = new Dictionary<GameObject, List<GameObject>>();
    private GameObject[] bottles;
    private Dictionary<GameObject, bool> isTransparent = new Dictionary<GameObject, bool>();

    void Start()
    {
        bottles = GameObject.FindGameObjectsWithTag("bottle");

        var syncMapping = bottleSync.GetMasterToSubMapping();
        foreach (var entry in syncMapping)
        {
            GameObject master = entry.Key;
            GameObject sub = entry.Value;

            // master
            var masterRenderer = master.GetComponent<Renderer>();
            if (masterRenderer != null)
            {
                Material mat = new Material(masterRenderer.material); // 個別化
                SetMaterialTransparent(mat);
                masterRenderer.material = mat;

                materials[master] = mat;
                originalColors[master] = mat.GetColor("_BaseColor");
                isTransparent[master] = false;
            }

            // sub
            var subRenderer = sub.GetComponent<Renderer>();
            if (subRenderer != null)
            {
                Material subMat = new Material(subRenderer.material);
                SetMaterialTransparent(subMat);
                subRenderer.material = subMat;
            }

            if (!masterToSubMapping.ContainsKey(master))
                masterToSubMapping[master] = new List<GameObject>();
            masterToSubMapping[master].Add(sub);
        }

        if (ReachabilityFullObject != null)
            reachabilityCollider = ReachabilityFullObject.GetComponent<Collider>();
    }

    void Update()
    {
        GameObject hit = _rayManager.hitObject;

        foreach (var bottle in bottles)
        {
            if (!materials.ContainsKey(bottle)) continue;

            // ①エリア内か判定
            bool isInsideArea = false;
            if (reachabilityCollider != null)
            {
                var point = bottle.transform.position;
                if (reachabilityCollider.bounds.Contains(point))
                {
                    isInsideArea = true;
                }
                else if (reachabilityCollider is MeshCollider mc && mc.convex)
                {
                    var closest = mc.ClosestPoint(point);
                    if (Vector3.Distance(closest, point) < 1e-4f)
                        isInsideArea = true;
                }
            }

            // ②色決定
            Color targetColor;
            bool shouldBeTransparent;

            if (!isInsideArea)
            {
                // エリア外は何があってもOtherColor
                targetColor = OtherColor;
                shouldBeTransparent = true;
            }
            else
            {
                // 範囲内のみRay/掴みによる分岐
                if (hit == null)
                {
                    // Rayヒットなし→範囲内は全部OriginalColor
                    targetColor = OriginalColor;
                    shouldBeTransparent = false;
                }
                else if (hit == bottle)
                {
                    // 範囲内＆ヒット→PickColor
                    targetColor = PickColor;
                    shouldBeTransparent = false;
                }
                else
                {
                    // 範囲内＆ヒットしたが自分は対象外→OtherColor
                    targetColor = OtherColor;
                    shouldBeTransparent = true;
                }
            }

            // あとは元のフェードやサブボトル処理同様

            Material mat = materials[bottle];
            if (!isTransparent.ContainsKey(bottle) || isTransparent[bottle] != shouldBeTransparent)
            {
                if (shouldBeTransparent)
                    SetMaterialTransparent(mat);
                else
                    SetMaterialOpaque(mat);

                isTransparent[bottle] = shouldBeTransparent;
            }
            Color currentColor = mat.GetColor("_BaseColor");
            Color newColor = Color.Lerp(currentColor, targetColor, Time.deltaTime * fadeSpeed);
            mat.SetColor("_BaseColor", newColor);

            // サブボトルへの反映も同様
            if (masterToSubMapping.TryGetValue(bottle, out List<GameObject> subList))
            {
                foreach (var subBottle in subList)
                {
                    if (subBottle == null) continue;
                    var subRenderer = subBottle.GetComponent<Renderer>();
                    if (subRenderer != null)
                    {
                        subRenderer.material.color = newColor;
                        if (shouldBeTransparent)
                            SetMaterialTransparent(subRenderer.material);
                        else
                            SetMaterialOpaque(subRenderer.material);
                    }
                }
            }
        }
    }


    // 透明・不透明のマテリアル設定
    void SetMaterialTransparent(Material mat)
    {
        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void SetMaterialOpaque(Material mat)
    {
        mat.SetFloat("_Surface", 0); // Opaque
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }
}
*/