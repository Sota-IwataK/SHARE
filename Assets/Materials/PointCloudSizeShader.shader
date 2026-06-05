Shader "Custom/PointCloudSizeShader"
{
    Properties{
        _PointSize("Point Size", Range(10, 100.0)) = 100.0
    }

        SubShader{
            Tags { "RenderType" = "Opaque" }
            LOD 100

            Pass {
                CGPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                #include "UnityCG.cginc"

                struct appdata_t {
                    float4 vertex : POSITION;
                };

                struct v2f {
                    float4 vertex : SV_POSITION;
                    float pointSize : POINTSIZE;
                };

                float _PointSize;

                v2f vert(appdata_t v) {
                    v2f o;
                    o.vertex = UnityObjectToClipPos(v.vertex);
                    o.pointSize = _PointSize;
                    return o;
                }

                half4 frag(v2f i) : SV_Target {
                    return half4(1, 1, 1, 1); // White color
                }
                ENDCG
            }
    }
}
