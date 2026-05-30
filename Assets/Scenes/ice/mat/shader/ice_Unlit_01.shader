Shader "Custom/ice_Unlit_01"
{
    Properties
    {
        [Header(Base Color)]
        [Space(5)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}

        [Header(Emission)]
        [Space(5)]
        [HDR] _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _EmissionStrength ("Emission Strength", Range(0, 10)) = 1.0

        [Header(Normal Map)]
        [Space(5)]
        [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _NormalStrength ("Normal Strength", Range(0, 10)) = 1.0
        
        [Header(Specular)]
        [Space(5)]
        _SpecularColor ("Specular Color", Color) = (1, 1, 1, 1)
        _Shininess ("Shininess", Range(0.1, 256)) = 32.0
        _SpecularIntensity ("Specular Intensity", Range(0, 2)) = 1.0

        [Header(Normal Sharp)]
        [Space(5)]
        _NormalSharpMap ("Normal Sharp Map", 2D) = "black" {}
        _NormalSharpIntensity ("Normal Sharp Intensity", Range(0, 5)) = 1.0
        _NormalSharpColor ("Normal Sharp Color", Color) = (1, 1, 1, 1)

        [Header(Fresnel)]
        [Space(5)]
        _FresnelScale ("Fresnel Scale", Range(0, 4)) = 3
        _FresnelIntensity ("Fresnel Intensity", Range(0, 5)) = 1
        _FresnelColor ("Fresnel Color", Color) = (1, 1, 1, 1)

        [Header(Fade Effect)]
        [Space(5)]
        _FadeRange ("Fade Range", Range(0, 1)) = 0.5
        _FadeSpeed ("Fade Speed", Float) = 1
        _BottomColor ("Bottom Color", Color) = (1, 1, 1, 1)

        [Header(Noise)]
        [Space(5)]
        _NoiseMap ("Noise Map", 2D) = "black" {}
        _NoiseMapTilling ("Noise Map Tilling", Float) = 1
        _NoiseColor ("Noise Color", Color) = (1, 1, 1, 1)
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }
        LOD 100

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // URP核心库
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // 顶点输入结构
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
            };

            // 顶点输出结构（片段输入）
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 tangentWS : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 positionWS : TEXCOORD4;
                float3 positionOS : TEXCOORD5;
            };

            // 纹理和采样器声明
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D( _NormalSharpMap);
            SAMPLER(sampler_NormalSharpMap);

            TEXTURE2D(_NoiseMap);
            SAMPLER(sampler_NoiseMap);

            // 非贴图参数使用CBUFFER以支持SRP Batcher
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _MainTex_ST;
                half _NormalStrength;
                half4 _EmissionColor;
                half _EmissionStrength;
                half4 _SpecularColor;
                half _Shininess;
                half _SpecularIntensity;
                half _NormalSharpIntensity;
                half4 _NormalSharpColor;
                half _FresnelScale;
                half4 _FresnelColor;
                half _FresnelIntensity;
                half4 _BottomColor;
                half _FadeRange;
                half _FadeSpeed;
                half4 _NoiseColor;
                half _NoiseMapTilling;
            CBUFFER_END

            // 辅助函数：从法线贴图获取世界空间法线
            half3 GetWorldNormalFromNormalMap(Varyings input, half3 normalWS, half4 tangentWS, half3 normalTS)
            {
                // 构建TBN矩阵
                half3 bitangentWS = cross(normalWS, tangentWS.xyz) * tangentWS.w;
                half3x3 TBN = half3x3(tangentWS.xyz, bitangentWS, normalWS);
                
                // 将切线空间法线转换到世界空间
                return normalize(mul(normalTS, TBN));
            }

            // Blinn-Phong高光反射计算函数
            half3 CalculateBlinnPhongSpecular(half3 normalWS, half3 viewDirWS, half3 lightDir, half3 lightColor, 
                                              half3 specularMap, half3 specularColor, half shininess, half specularIntensity)
            {
                // 计算半角向量
                half3 halfVec = normalize(lightDir + viewDirWS);
                
                // 计算Blinn-Phong高光强度
                half blinnPhong = pow(max(0.0, dot(normalWS, halfVec)), shininess);
                
                // 应用高光贴图、颜色和强度
                half3 specular = lightColor * specularColor * specularMap * blinnPhong * specularIntensity;
                
                return specular;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // 顶点位置转换
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.positionOS = input.positionOS.xyz;
                
                // UV坐标转换
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                
                // 法线和切线转换到世界空间
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                output.bitangentWS = normalInput.bitangentWS;
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 采样主纹理
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // 采样法线贴图并转换到世界空间
                half4 normalMap = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                half3 normalTS = UnpackNormalScale(normalMap, _NormalStrength);
                half3 normalWS = GetWorldNormalFromNormalMap(input, input.normalWS, half4(input.tangentWS, 1), normalTS);
                // 采样自发光贴图
                half3 emission = mainTex.aaa * _EmissionColor.rgb * _EmissionStrength;
                
                // 获取主光源信息
                Light mainLight = GetMainLight();
                half3 lightDir = normalize(mainLight.direction);
                half3 lightColor = mainLight.color;
                half lightAttenuation = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                
                // 归一化视线方向
                half3 viewDirWS = normalize( GetWorldSpaceViewDir(input.positionWS));
                
                // 基础漫反射（N·L）
                half NdotL = saturate(dot(normalWS, lightDir));
                
                half3 diffuse = _BaseColor.rgb* mainTex.rgb * NdotL * lightAttenuation;
                
                // Blinn-Phong高光反射
                
                 // 计算半角向量
                half3 halfVec = normalize(lightDir + viewDirWS);

                // 计算Blinn-Phong高光强度
                half blinnPhong = pow(max(0.0, dot(normalWS, halfVec)), _Shininess);
                // 应用高光贴图、颜色和强度
                half3 specular =  _SpecularColor.rgb * blinnPhong * _SpecularIntensity;
                
                // 环境光
                half3 ambient = 0.33 * mainTex.rgb;

                //菲尼尔
                half fresnel = pow(saturate(1-dot(viewDirWS, normalWS)),_FresnelScale);
                half3 fresnelColor = fresnel*_FresnelIntensity*_FresnelColor.rgb;

                //转折高光
                half3 sharpSpecular = SAMPLE_TEXTURE2D(_NormalSharpMap, sampler_NormalSharpMap, input.uv).rgb;

                half3 sharpHiLight = sharpSpecular* _NormalSharpColor*_NormalSharpIntensity;

                //Noise
                half noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, input.uv*_NoiseMapTilling).r;
                half3 NoiseColor = noise*_NoiseColor;

                // 组合最终颜色：环境光 + 漫反射 + 高光反射 + 自发光
                half3 bzd = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv.xy+float2(0.2,0.2));
                
                half3 finalColor = ambient + diffuse+specular +emission+fresnelColor+NoiseColor+sharpHiLight;
                // +specular +emission+fresnelColor+NoiseColor;
                float YFade = smoothstep(0,_FadeRange,frac(input.positionOS.y/7+0.2));
                YFade = pow(YFade,_FadeSpeed);
                finalColor = lerp(_BottomColor.rgb*finalColor, finalColor, YFade);
                // 输出最终颜色（保持Alpha通道）
                return half4(finalColor, mainTex.a);
            }
            ENDHLSL
        }
    }
}
