Shader "Custom/Ground"
{
    Properties
    {
        _GrassMap ("Grass Map", 2D) = "white" {}
        _GrassColor ("Grass Color", Color) = (1, 1, 1, 1)
        _SoilMap ("Soil Map", 2D) = "white" {}
        _SoilColor ("Soil Color", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _GrassORSoilRT ("Grass / Soil Mask (R)", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        Cull Back
        ZWrite On

        // Keep the material buffer identical across all passes for SRP batching.
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _GrassMap_ST;
            float4 _SoilMap_ST;
            half4 _GrassColor;
            half4 _SoilColor;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "GroundForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GroundVertex
            #pragma fragment GroundFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            TEXTURE2D(_GrassMap);
            SAMPLER(sampler_GrassMap);
            TEXTURE2D(_SoilMap);
            SAMPLER(sampler_SoilMap);
            TEXTURE2D(_GrassORSoilRT);
            SAMPLER(sampler_GrassORSoilRT);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings GroundVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 GroundFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 grassUV = TRANSFORM_TEX(input.uv, _GrassMap);
                float2 soilUV = TRANSFORM_TEX(input.uv, _SoilMap);
                half3 grassColor = SAMPLE_TEXTURE2D(_GrassMap, sampler_GrassMap, grassUV).rgb * _GrassColor.rgb;
                half3 soilColor = SAMPLE_TEXTURE2D(_SoilMap, sampler_SoilMap, soilUV).rgb * _SoilColor.rgb;

                // The interaction mask uses the original mesh UVs: 0 = soil, 1 = grass.
                half mask = saturate(SAMPLE_TEXTURE2D(_GrassORSoilRT, sampler_GrassORSoilRT, input.uv).r);
                half3 landColor = lerp(soilColor, grassColor, mask);

                // Evaluate per pixel so cascade boundaries and screen-space shadows work.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                half shadow = MainLightRealtimeShadow(shadowCoord);
                #if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
                    shadow = lerp(shadow, 1.0h, GetMainLightShadowFade(input.positionWS));
                #endif

                // Preserve the original texture-color-times-shadow appearance.
                half3 color = MixFog(landColor * shadow, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
