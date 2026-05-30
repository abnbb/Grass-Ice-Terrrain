Shader "Hidden/GrassBrush"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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

            half _isGrass;
            float2 _pointUV;
            half _BrushSize;
            half _BrushHarness;//(0-1)

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
                half landMask = tex2D(_MainTex,i.uv).r;
                float d = length(_pointUV-i.uv);
                half drawing = smoothstep(_BrushSize,_BrushSize*_BrushHarness,d);
                return lerp(landMask-drawing ,landMask+drawing ,_isGrass);
            }
            ENDCG
        }
    }
}
