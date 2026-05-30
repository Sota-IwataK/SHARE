Shader "Custom/PointCloud"
{
    Properties
    {
        _PointSize("Point Size (px)", Float) = 20.0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "UnityCG.cginc"

            float _PointSize;
            float4 _ScreenParams; // x=width, y=height, z=1/width, w=1/height

            struct Attributes
            {
                float3 position : POSITION;
                float4 color    : COLOR;
            };

            struct Varyings
            {
                float4 pos   : SV_POSITION;
                float4 color : COLOR;
            };

            // 頂点シェーダー：オブジェクト→クリップ空間
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                float4 worldPos = float4(IN.position, 1);
                OUT.pos   = TransformObjectToHClip(worldPos);
                OUT.color = IN.color;
                return OUT;
            }

            // ジオメトリシェーダー：１点→クワッド４頂点
            [maxvertexcount(4)]
            void geom(point Varyings IN[1], inout TriangleStream<Varyings> triStream)
            {
                float4 clipPos = IN[0].pos;
                float4 col     = IN[0].color;
                // ピクセルサイズをクリップ空間のオフセットに変換
                float2 ps = _PointSize * float2(_ScreenParams.z, _ScreenParams.w);

                // クワッドの４角オフセット
                float2 offs[4] = {
                    float2(-1, -1) * ps,
                    float2( 1, -1) * ps,
                    float2( 1,  1) * ps,
                    float2(-1,  1) * ps
                };

                for (int i = 0; i < 4; i++)
                {
                    Varyings OUT = IN[0];
                    OUT.pos   = clipPos + float4(offs[i], 0, 0);
                    triStream.Append(OUT);
                }
            }

            // フラグメントシェーダー：色出力
            half4 frag(Varyings IN) : SV_Target
            {
                return IN.color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
