Shader "Custom/Ground"
{
    Properties
    {
        _GrassMap ("GrassMap", 2D) = "white" {}
        _GrassColor("Grass Color", Color)=(1,1,1,1)
        _SoilMap ("Soil Map", 2D) = "white" {}
        _SoilColor("Soil Cl=olor", Color)=(1,1,1,1)
        _GrassORSoilRT("GrassORSoilRT",2D)="white"{}
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
            #pragma multi_compile_fwdbase
            #include "UnityCG.cginc"
            #include "AutoLight.cginc"


            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                SHADOW_COORDS(1)
                float4 vertex : SV_POSITION;
            };

            sampler2D _GrassMap;
            float4 _GrassMap_ST;
            sampler2D _SoilMap;
            sampler2D _GrassORSoilRT;

            half4 _GrassColor;
            half4 _SoilColor;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                TRANSFER_SHADOW(o);
                o.uv = v.uv;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                // sample the texture
                half3 GrassColor = tex2D(_GrassMap, i.uv*_GrassMap_ST.xy+_GrassMap_ST.zw).rgb*_GrassColor.rgb;
                half3 SoilColor = tex2D(_SoilMap, i.uv*_GrassMap_ST.xy+_GrassMap_ST.zw).rgb*_SoilColor.rgb;
                half GrassORSoilMask = tex2D(_GrassORSoilRT,i.uv).r;
                half3 landColor = lerp(SoilColor,GrassColor,GrassORSoilMask);

                fixed shadow = SHADOW_ATTENUATION(i);
                return half4(landColor*shadow,1);
            }
            ENDCG
        }
    }
}
