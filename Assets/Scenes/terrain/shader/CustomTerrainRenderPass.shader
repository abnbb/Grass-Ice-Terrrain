Shader "Custom/CustomTerrainRenderPass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _MaskTex("Mask", 2D) = "black"{}
        _MaskOffset("Mask Offset", float) = 0
        _CurrentDiff("Current Diff", 2D) = "black"{}
        _CurrentNormal("Current Normal", 2D) = "black"{}
        _metallic("metallic", float) = 0
        _roughness("roughness", float) = 0
        _TillingOffset("TillingOffset", Vector)=(1,1,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

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
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            sampler2D _MaskTex;
            sampler2D _CurrentDiff;
            sampler2D _CurrentNormal;
            
            float _MaskOffset;
            float _metallic;
            float _roughness;
            float4 _TillingOffset;


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half4 col = tex2D(_MainTex, i.uv);
                half4 diff = tex2D(_CurrentDiff, i.uv);
                half3 normal = tex2D(_CurrentNormal, i.uv).rgb;
                float mask = tex2D(_MaskTex, i.uv).r;
                
                mask -= _MaskOffset;
                mask = step(0.09, mask);
                half3 color = lerp(col.rgb,diff.rgb, mask);
                return half4(color,1);
            }
            ENDCG
        }
    }
}
