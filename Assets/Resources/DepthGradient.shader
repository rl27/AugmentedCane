Shader "Unlit/DisplayDepth"
{
    Properties
    {
        _MainTex ("_MainTex", 2D) = "white" {}
        _DepthTex ("_DepthTex", 2D) = "green" {}
    }
    SubShader
    {
        Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
        Blend SrcAlpha OneMinusSrcAlpha
        // No culling or depth
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float2 texcoord : TEXCOORD1;
                float4 vertex : SV_POSITION;

            };

            float4x4 _DisplayMat;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                #if !UNITY_UV_STARTS_AT_TOP
                o.uv.y = 1-o.uv.y;
                #endif

                //we need to adjust our image to the correct rotation and aspect.
                o.texcoord = mul(float3(o.uv, 1.0f), _DisplayMat).xy;
                return o;
            }

            sampler2D _MainTex;
            sampler2D _DepthTex;

            float3 HSVtoRGB(float3 arg1)
            {
                float4 K = float4(1.0h, 2.0h / 3.0h, 1.0h / 3.0h, 3.0h);
                float3 P = abs(frac(arg1.xxx + K.xyz) * 6.0h - K.www);
                return arg1.z * lerp(K.xxx, saturate(P - K.xxx), arg1.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float depth = tex2D(_DepthTex, i.texcoord).r;

                const float MAX_VIEW_DISP = 4.0f;
                const float scaledDisparity = 1.0f / depth;
                const float normDisparity = scaledDisparity / MAX_VIEW_DISP;

                float lerpFactor = depth / 8;
                float hue = lerp(0.70h, -0.15h, saturate(lerpFactor));
                if (hue < 0.0h)
                {
                    hue += 1.0h;
                }

                float3 color = float3(hue, 0.9h, 0.6h);
                return float4(HSVtoRGB(color), 1.0h);

                //return float4(normDisparity,normDisparity,normDisparity,0.8);
            }
            ENDCG
        }
    }
}