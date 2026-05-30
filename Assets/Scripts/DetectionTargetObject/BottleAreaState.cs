
using RosSharp.RosBridgeClient;
using RosSharp.Urdf;
using System.Collections.Generic;
using UnityEngine;
using RosSharp.RosBridgeClient.MessageTypes.Std;   // Float32MultiArray

public class BottleAreaState : MonoBehaviour
{
    // 現在の inside / outside の状態
    [field: SerializeField, Tooltip("エリア内かどうか（読み取り専用）")]
    public bool IsInside { get; private set; }
    public bool IsHit { get; private set; }
    // public ObjectHit ObjectHit;
    public BottleHitMapper BottleHitMapper;
    public IRM_SerectObjectPublisher irmPublisher;
    

    [ColorUsage(false, true)] public Color PickColor;      // Inside ＆ ROS同定
    [ColorUsage(false, true)] public Color OtherColor;     //  !inside　または　他にPickあるとき
    [ColorUsage(false, true)] public Color OutSideSerectColor;     // !insideかつROS同定

    private Color originalColor;

    public float fadeSpeed = 5f;
    // 内部キャッシュ
    private Material mat;
    private string colorProp;
    private Coroutine fadeRoutine;
    public GameObject targetUIPrefab;
    public GameObject targetUIPrefab_outside;
    
    private GameObject currentTargetUI = null;
    public bool isHitted = false;
    private bool isTransparent = false;
    private bool hasPublished = false;   // 送信済みフラグ

 

    [Range(0f, 1f)] public float otherAlpha = 0.5f;
    // SelectObject から「この色で Override してほしい」と言われたときに使う
     bool manualOverride = false;
     Color manualColor;
   public GameObject Origin;

    public void OverrideColor(Color c)
     {
         manualOverride = true;
         manualColor     = c;
     }
     public void ClearOverride()
     {
         manualOverride = false;
     }


    void Awake()
    {
        // 1) Renderer の sharedMaterial をコピーして独立
        var rend = GetComponent<Renderer>();
        mat = new Material(rend.sharedMaterial);
        rend.material = mat;

        // 2) カラープロパティ名を自動判別
        colorProp = mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";

        // 3) 本来の色をマテリアルから取得
        originalColor = mat.GetColor(colorProp);
        // シーン中から ObjectHit を自動取得
        // if (ObjectHit == null)
        //     ObjectHit = FindObjectOfType<ObjectHit>();
        if (BottleHitMapper == null)
                BottleHitMapper = FindObjectOfType<BottleHitMapper>();
        if (irmPublisher == null)
            irmPublisher = FindObjectOfType<IRM_SerectObjectPublisher>();


        
        if (Origin == null)
        {
            Origin = GameObject.Find("origin_base");
            if (Origin == null)
            {
                Debug.LogError($"[BottleAreaState] Hierarchy 上に名前 \"Origin\" の GameObject が見つかりません。");
            }
        }

        IsInside = true;
    }


    public void SetInside(bool isInside)
    {
        IsInside = isInside;
    }
    public void SetHit(bool hit)
    {
        IsHit = hit;
    }


    public void Update()
    {

        GameObject globalHit = BottleHitMapper != null ? BottleHitMapper.hitObject : null;
        bool isThisHit = (globalHit == this.gameObject);
        bool anyOtherHit = (globalHit != null && !isThisHit);



        // Outside かつ 自分が Roshit されていない場合、または他オブジェクトが Roshit の場合は透明
        bool shouldBeTransparent = (!IsInside && !isThisHit) || anyOtherHit;
        if (shouldBeTransparent != isTransparent)
        {
            if (shouldBeTransparent) SetMaterialTransparent(mat);
            else                   SetMaterialOpaque(mat);
            isTransparent = shouldBeTransparent;
        }

        Color targetColor;


        // 送信条件：ROS同定かつOutside
        bool shouldPublish = isThisHit && !IsInside;
  

        if (shouldPublish)
        {
            // A) ROS同定かつOutside のとき
            if (!hasPublished)
            {
                // 初回だけ IRM に座標送信
                float[] coords = IRM_ROS_SelectMessage();
                irmPublisher?.SetCoords(coords);
                
            }
            // このときは常にこの色
            targetColor = OutSideSerectColor;
            // DestroyUI();
          
            if (!isHitted)
                {
                    UIset_outside();
                    isHitted = true;
                    
                
                }
        }
        else
        {
            // リセット：次のターゲット変更で再度 Publish できるように
            hasPublished = false;

            if (isThisHit && IsInside)
            {
                // B) ROS同定かつInside
                targetColor = PickColor;
                if (!isHitted)
                {
                    UIset();
                    isHitted = true;
                    
                
                }
            }
            else if (!IsInside || anyOtherHit)
            {
                // C) Outside または 他オブジェクト同定中
                targetColor = new Color(OtherColor.r, OtherColor.g, OtherColor.b, otherAlpha);
                DestroyUI();
                isHitted = false;
            }
            else
            {
                // D) Inside かつ非同定
                targetColor = originalColor;
                DestroyUI();
                isHitted = false;
            }
        }



        Color current = mat.GetColor(colorProp);
        Color next = Color.Lerp(current, targetColor, UnityEngine.Time.deltaTime * fadeSpeed);
        mat.SetColor(colorProp, next);

    }

    public void UIset()
    {
        if (currentTargetUI != null)
        {
            Destroy(currentTargetUI);
            currentTargetUI = null;
        }

        Vector3 bottlePos = transform.position;
        Transform camT = Camera.main.transform;
        Vector3 dirToCam = (camT.position - bottlePos).normalized;

        float forwardOffset = 0.15f;  // 10cm ��O��                                      
        float heightOffset = 0.1f;   // 10cm ���

        Vector3 uiPos = bottlePos
                        + dirToCam * forwardOffset
                        + Vector3.up * heightOffset;


        Vector3 toCamFlat = camT.position - uiPos;
        toCamFlat.y = 0;
        Quaternion uiRot = Quaternion.LookRotation(toCamFlat.normalized, Vector3.up);
        currentTargetUI = Instantiate(
                                        targetUIPrefab,
                                        uiPos,
                                        uiRot,
                                        this.transform
                                    );
        currentTargetUI.transform.LookAt(camT.position, Vector3.up);

    }

    public void UIset_outside()
    {
        if (currentTargetUI != null)
        {
            Destroy(currentTargetUI);
            currentTargetUI = null;
        }

        Vector3 bottlePos = transform.position;
        Transform camT = Camera.main.transform;
        Vector3 dirToCam = (camT.position - bottlePos).normalized;

        float forwardOffset = 0.15f;  // 10cm ��O��                                      
        float heightOffset = 0.1f;   // 10cm ���

        Vector3 uiPos = bottlePos
                        + dirToCam * forwardOffset
                        + Vector3.up * heightOffset;


        Vector3 toCamFlat = camT.position - uiPos;
        toCamFlat.y = 0;
        Quaternion uiRot = Quaternion.LookRotation(toCamFlat.normalized, Vector3.up);
        currentTargetUI = Instantiate(
                                        targetUIPrefab_outside,
                                        uiPos,
                                        uiRot,
                                        this.transform
                                    );
        currentTargetUI.transform.LookAt(camT.position, Vector3.up);

    }

    public void DestroyUI()
    {
        if (currentTargetUI != null)
        {
            Destroy(currentTargetUI);
            currentTargetUI = null;
        }
        isHitted = false;
    }

    public float[] IRM_ROS_SelectMessage()
    {
        var selectCoords = new List<float>();
        if (Origin == null)
        {
            Debug.LogError("Origin が設定されていません！");
            return selectCoords.ToArray();
        }

        // YouBot 向けに追加したいオフセット
        /* Vector3 axisOffset = new Vector3(-0.123f, 0.056f, 0f);*/

        Vector3 axisOffset = new Vector3(0.0f, 0.0f, 0.0f);

        // １度だけ取得しておく Origin のワールド座標
        Vector3 originWorld = Origin.transform.position;

    
        // 1) ボトルのワールド座標
        Vector3 bottleWorld = this.transform.position;

        // 2) ワールド差分で相対位置を計算
        Vector3 relative = bottleWorld - originWorld;

        // 3) オフセットを加算
        Vector3 adjusted = relative + axisOffset;

        // 4) YouBot 向けに軸反転・入れ替え
        float youbot_x = -adjusted.x;
        float youbot_y = -adjusted.z;
        float youbot_z = adjusted.y;

        // 5) 追加
        selectCoords.Add(youbot_x);
        selectCoords.Add(youbot_y);
        selectCoords.Add(youbot_z);
        

        return selectCoords.ToArray();
    }


    // ─────────────────────────────────────────────────
    void SetMaterialTransparent(Material m)
    {
        m.SetFloat("_Surface", 1);
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void SetMaterialOpaque(Material m)
    {
        m.SetFloat("_Surface", 0);
        m.SetOverrideTag("RenderType", "Opaque");
        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        m.SetInt("_ZWrite", 1);
        m.DisableKeyword("_ALPHATEST_ON");
        m.DisableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }
    
}

