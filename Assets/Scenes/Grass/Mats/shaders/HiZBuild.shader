Shader "Hidden/HiZBuild"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0: 复制深度并线性化（可选，如果深度已经线性则跳过）
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct appdata{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };

            struct v2f{
                float4 vertex : SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            v2f vert(appdata i){
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.uv = i.uv;
                return o;
            }

            float frag (v2f i) : SV_Target
            {
                float depth = tex2D(_MainTex, i.uv).r;
                // 如果是非线性深度（如 Reversed Z）需要转换为线性 0~1
                // 这里假设输入已是线性深度，直接返回
                return depth;
            }
            ENDCG
        }

        Pass // 1: 下采样并取最大值
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            struct appdata{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };

            struct v2f{
                float4 vertex : SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            v2f vert(appdata i){
                v2f o;
                o.vertex = UnityObjectToClipPos(i.vertex);
                o.uv = i.uv;
                return o;
            }

            float frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 texel = _MainTex_TexelSize.xy;
                // 采样 2x2 区域四个像素，取最大值
                float d1 = tex2D(_MainTex, uv + float2(-0.5, -0.5) * texel).r;
                float d2 = tex2D(_MainTex, uv + float2( 0.5, -0.5) * texel).r;
                float d3 = tex2D(_MainTex, uv + float2(-0.5,  0.5) * texel).r;
                float d4 = tex2D(_MainTex, uv + float2( 0.5,  0.5) * texel).r;
                return max(max(d1, d2), max(d3, d4));
            }
            ENDCG
        }
    }
}