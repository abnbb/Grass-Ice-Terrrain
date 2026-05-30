Shader "Custom/ModleGrass"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _BaseColor("Base Color", Color)=(1,1,1,1)
        _NoiseMap ("Noise Map", 2D) = "black" {}
        _NoiseInten ("Noise Intensity", Float) = 1
        _Specualr("specular", Range(0,64))=8
        _SpecularIntensity("Specular Intensity", float)=1
        _SpecualrColor("Specualr Color", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        Cull Back

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT

            #pragma shader_feature _MAIN_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float3 normal : TEXCOORD2;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _NoiseInten;
                float _Specualr;
                float _SpecularIntensity;
                float4 _SpecualrColor;
                float4 _BaseColor;
            CBUFFER_END

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                // 计算世界坐标（URP 推荐方式）
                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);

                // 流动 UV（用于采样噪声图）
                float2 flowUV1 = worldPos.xz * 0.3 + float2(_Time.y * 0.1, 0);
                float2 flowUV2 = worldPos.xz * 0.1 + float2(_Time.y * 0.1, 0);
                float2 flowUV3 = worldPos.xz * 0.3 + float2(_Time.y * 0.1, 0);

                // URP 采样纹理的 Level 版本（避免梯度错误）
                float noise1 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV1, 0).r;
                float noise2 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV2, 0).r;
                float noise3 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV3, 0).r;

                float3 noiseDir = float3(noise1, noise2, noise3);
                worldPos += noiseDir * _NoiseInten * v.color.r;
                o.worldPos = worldPos;
                o.normal = TransformObjectToWorldNormal(v.normal);
                o.vertex = TransformWorldToHClip(worldPos);
                o.uv = v.uv;
                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, float2(i.uv.x, 1 - i.uv.y));
                col*=_BaseColor;
                float4 shadowCoord = TransformWorldToShadowCoord(i.worldPos);
                Light mainLight = GetMainLight(shadowCoord);
                float shadowAtten = mainLight.shadowAttenuation;  // 阴影不存在时自动为 1.0

                float3 L = mainLight.direction;
                float3 N = normalize(i.normal);
                float NdotL = saturate(dot(N,L));
                float3 V = normalize(_WorldSpaceCameraPos-i.worldPos);
                float3 H = normalize(L+V);
                half3 diffuse = NdotL*mainLight.color*col.rgb;
                half3 ambient = 0.2*col.rgb;
                float specu = pow(max(0,dot(N, H)),_Specualr);
                half3 specular = specu*_SpecularIntensity*_SpecualrColor.rgb;
                half3 finalColor = diffuse+specular;
                finalColor*=shadowAtten;
                finalColor+=ambient;
                return half4(finalColor,1);
            }
            ENDHLSL
        }

        //shadowCaster
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);
            CBUFFER_START(UnityPerMaterial)
                float _NoiseInten;
            CBUFFER_END

            // 顶点动画函数（与主Pass相同）
            float3 ApplyVertexAnimation(float3 worldPos, float vertexColorR)
            {
                float2 flowUV1 = worldPos.xz * 0.3 + float2(_Time.y * 0.1, 0);
                float2 flowUV2 = worldPos.xz * 0.1 + float2(_Time.y * 0.1, 0);
                float2 flowUV3 = worldPos.xz * 0.3 + float2(_Time.y * 0.1, 0);

                float noise1 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV1, 0).r;
                float noise2 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV2, 0).r;
                float noise3 = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, flowUV3, 0).r;

                float3 noiseDir = float3(noise1, noise2, noise3);
                return noiseDir * _NoiseInten * vertexColorR;
            }

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);

                float3 worldPos = TransformObjectToWorld(v.vertex.xyz);
                worldPos += ApplyVertexAnimation(worldPos, v.color.r);

                // 阴影投射需要应用深度偏移（Unity处理）
                o.vertex = TransformWorldToHClip(worldPos);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDir = normalize(_MainLightPosition.xyz);
                    o.vertex = ApplyShadowBias(o.vertex, worldPos, lightDir, 0);
                #else
                    float3 lightDir = normalize(_MainLightPosition.xyz);
                    o.vertex = ApplyShadowBias(o.vertex, worldPos, lightDir, 0);
                #endif

                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return 0; // 深度值已经在顶点阶段写入
            }
            ENDHLSL
        }

    }
}