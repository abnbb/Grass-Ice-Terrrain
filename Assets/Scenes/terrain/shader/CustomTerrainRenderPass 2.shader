Shader "Custom/CustomTerrainRenderPass2"
{
    Properties
    {
        _MaskTex("Mask", 2D) = "black"{}
        _TillingOffset("Tilling", Vector)=(1,1,0,0)
        _Diff1 ("Diff1", 2D) = "white" {}
        _metallic1("metallic1", float) = 0
        _roughness1("roughness1", float) = 0

        _Diff2 ("Diff2", 2D) = "white" {}
        _metallic2("metallic2", float) = 0
        _roughness2("roughness2", float) = 0

        _Diff3 ("Diff3", 2D) = "white" {}
        _metallic3("metallic3", float) = 0
        _roughness3("roughness3", float) = 0

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

            sampler2D _MaskTex;

            sampler2D _Diff1;
            sampler2D _Diff2;
            sampler2D _Diff3;

            float _metallic1;
            float _roughness1;
            float _metallic2;
            float _roughness2;
            float _metallic3;
            float _roughness3;

            float4 _TillingOffset;


            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                float2 uv = i.uv*_TillingOffset.xy+_TillingOffset.zw;
                half4 diff1 = tex2D(_Diff1, uv);
                half4 diff2 = tex2D(_Diff2, uv);
                half4 diff3 = tex2D(_Diff3, uv);
                float4 mask = tex2D(_MaskTex, uv);
                
                half3 finalColor = lerp(0, diff1, mask.r);
                finalColor = lerp(0, diff1, mask.r);
                finalColor = lerp(finalColor, diff2, mask.g);
                finalColor = lerp(finalColor, diff3, mask.b);
                // half3 color = lerp(col.rgb,diff.rgb, mask);
                return half4(finalColor,1);
            }
            ENDCG
        }
    }
}
