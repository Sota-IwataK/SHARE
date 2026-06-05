Shader "CustomRenderTexture/MetaPointCloudShader"
{
     Properties
    {
        _Color("Main Color", Color) = (1,1,1,1)
        _PointSize("Point Size (px)", Float) = 10.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                fixed4 color : COLOR;
            };

            float4 _Color;
            float  _PointSize;

            v2f vert(appdata v)
            {
                v2f o;
                // １）オブジェクト→クリップ空間
                o.pos   = UnityObjectToClipPos(v.vertex);
                // ２）頂点カラー × プロパティカラー
                o.color = v.color * _Color;

                // ３）ポイントサイズをセット（Meta Quest 上の GLES3 で有効）
                #if defined(SHADER_API_GLES3) || defined(SHADER_API_GLES)
                    gl_PointSize = _PointSize;
                #endif

                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // そのまま頂点色を出力
                return i.color;
            }
            ENDCG
        }
    }
    FallBack Off
}
