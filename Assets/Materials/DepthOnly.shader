Shader "ModelTracker/DepthOnly" {
    SubShader {
        Tags { "RenderType"="Opaque" }
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata {
                float4 vertex : POSITION;
            };
            
            struct v2f {
                float4 pos : SV_POSITION;
                float depth : TEXCOORD0;
            };
            
            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                // 使用更直接的深度计算方法，获取视图空间中的z值
                // 不乘以_ProjectionParams.w，保留原始深度范围
                float3 viewPos = UnityObjectToViewPos(v.vertex);
                o.depth = -viewPos.z; // 直接使用视图空间的z值（取反使其为正值）
                return o;
            }
            
            float4 frag(v2f i) : SV_Target {
                return float4(i.depth, i.depth, i.depth, 1.0);
            }
            ENDCG
        }
    }
    FallBack Off
}