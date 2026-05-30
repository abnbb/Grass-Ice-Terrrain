Shader "Unlit/terrUnilt"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
        _NormalMap("Normal Map", 2D) = "white" {} // 标准法线贴图默认值建议给 "bump"
        _MRO("MRO", 2D)="white"{}
        _NormalStrength("Normal Strength", Range(0, 5)) = 1
        _metallic("Metallic", Range(0, 1)) = 0
        _Roughness("Roughness", Range(0, 1)) = 1
    }
    SubShader
    {
        // 既然带有光照计算，建议加上 UniversalPipeline 标签以保证渲染管线识别
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        Pass
        {
            // 给 Pass 命名，方便未来的渲染特性的识别或 ForwardLit 覆盖
            Name "ForwardLit"

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct appdata
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
            };

            struct v2f
            {
                float2 uv          : TEXCOORD0;
                float4 positionCS  : SV_POSITION;
                float3 normalWS    : TEXCOORD1;
                float3 tangentWS   : TEXCOORD2;
                float3 bitangentWS : TEXCOORD3;
                float3 positionWS  : TEXCOORD4;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_NormalMap);
            SAMPLER(sampler_NormalMap);

            TEXTURE2D(_MRO);
            SAMPLER(sampler_MRO);

            // 严格规范的 UnityPerMaterial 属性块，保证 SRP Batcher 合批合规
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _NormalMap_ST; 
                half _NormalStrength;
                half _metallic;
                half _Roughness;
            CBUFFER_END

            // 改良后的 TBN 矩阵转换函数：直接利用顶点传过来的副切线
            half3 TransformTangentNormalToWorld(v2f input, half3 normalTS)
            {
                // 保证切线空间向量的单位正交性
                half3 T = normalize(input.tangentWS);
                half3 B = normalize(input.bitangentWS);
                half3 N = normalize(input.normalWS);
                
                half3x3 TBN = half3x3(T, B, N);
                return normalize(mul(normalTS, TBN));
            }

            v2f vert (appdata input)
            {
                v2f output;
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = vertexInput.positionCS;
                output.positionWS = vertexInput.positionWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);

                // 使用 URP 内置函数安全提取世界空间下的法线、切线、副切线
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.normalWS = normalInput.normalWS;
                output.tangentWS = normalInput.tangentWS;
                output.bitangentWS = normalInput.bitangentWS;
                
                return output;
            }

            #define PI 3.14159265359

            // 1. D 项：GGX / Trowbridge-Reitz 法线分布函数
            // 计算微表面法线与宏观法线对齐的概率
            float DistributionGGX(float3 N, float3 H, float roughness)
            {
                float a = roughness * roughness;
                float a2 = a * a; // 迪士尼动画的平方映射，视觉线性感更好
                float NdotH = saturate(dot(N, H));
                float NdotH2 = NdotH * NdotH;

                float nom   = a2;
                float denom = (NdotH2 * (a2 - 1.0) + 1.0);
                denom = PI * denom * denom;

                return nom / max(denom, 0.000001); // 防止除以 0
            }

            // 2. G 项子函数：Schlick-GGX 几何遮蔽
            float GeometrySchlickGGX(float NdotV, float roughness)
            {
                // 直接光照下的 k 映射系数
                float r = (roughness + 1.0);
                float k = (r * r) / 8.0;

                float nom   = NdotV;
                float denom = NdotV * (1.0 - k) + k;

                return nom / max(denom, 0.000001);
            }

            // G 项：Smith 遮蔽函数
            // 同时考虑视线方向（V）和光线方向（L）的几何阴影和遮挡
            float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
            {
                float NdotV = saturate(dot(N, V));
                float NdotL = saturate(dot(N, L));
                float ggx2 = GeometrySchlickGGX(NdotV, roughness);
                float ggx1 = GeometrySchlickGGX(NdotL, roughness);

                return ggx1 * ggx2;
            }

            // 3. F 项：Fresnel-Schlick 菲涅尔方程
            // 计算光线在不同角度下的反射与折射比例
            float3 FresnelSchlick(float cosTheta, float3 F0)
            {
                return F0 + (1.0 - F0) * pow(saturate(1.0 - cosTheta), 5.0);
            }

            half4 frag (v2f input) : SV_Target
            {
                // 采样基础贴图与混贴输出的 MRO
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 mroSample = SAMPLE_TEXTURE2D(_MRO, sampler_MRO, input.uv);
                
                float3 albedo = mainTex.rgb;
                float metallic = _metallic;
                float roughness = mroSample.g*_Roughness;
                float occlusion = 1; // 环境光遮蔽

                // 解包法线并转换至世界空间（遵照方案A）
                half4 normalSample = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv);
                half3 normalTS;

                // normalTS = UnpackNormalScale(normalSample, _NormalStrength);

                // normalTS.xy = normalSample.ag * 2.0 - 1.0;
                // normalTS.z = max(1.0e-16, sqrt(1.0 - saturate(dot(normalTS.xy, normalTS.xy))));

                normalTS.xyz = normalSample.rgb * 2.0 - 1.0; // 标准的 RGB 解包方法，适用于 Unity 的法线贴图（R=X, G=Y, B=Z）
                normalTS.xy *= _NormalStrength; // 可调节的法线强度缩放
                float3 N = TransformTangentNormalToWorld(input, normalTS);

                // 计算方向向量
                float3 V = normalize(GetCameraPositionWS() - input.positionWS); // 视线方向
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);                      // 光照方向
                float3 H = normalize(V + L);                                    // 半角向量

                // 基础点积准备
                float NdotV = saturate(dot(N, V));
                float NdotL = saturate(dot(N, L));

                // 计算基础反射率 F0
                // 非金属（绝缘体）表面默认反射率约为 0.04；金属表面直接使用其 albedo 颜色作为基础反射率
                float3 F0 = float3(0.04, 0.04, 0.04); 
                F0 = lerp(F0, albedo, metallic);

                // 执行 Cook-Torrance BRDF 三大数学项计算
                float  NDF = DistributionGGX(N, H, roughness);   // D
                float  G   = GeometrySmith(N, V, L, roughness);  // G
                float3 F   = FresnelSchlick(saturate(dot(H, V)), F0); // F

                // 计算高光部分 (Specular BRDF)
                float3 numerator    = NDF * G * F;
                float denominator   = 4.0 * NdotV * NdotL;
                float3 kS = F;                                   // 镜面反射能量占比
                float3 specularBRDF = numerator / max(denominator, 0.001); // 最终高光项

                // 计算漫反射部分 (Diffuse BRDF)
                // 根据能量守恒定律，折射进入表面的光线占比 kD = 1.0 - kS
                float3 kD = float3(1.0, 1.0, 1.0) - kS;
                kD *= 1.0 - metallic;                            // 金属几乎不产生漫反射
                float3 diffuseBRDF = albedo / PI;                // 经典朗伯特漫反射

                // 结合主光源计算直接光照 (Direct Light)
                // mainLight.color 包含了光源的颜色和强度 (Intensity)
                float3 radiance = mainLight.color;
                float3 directLight = (kD * diffuseBRDF + specularBRDF) * radiance * NdotL;

                // 计算间接光环境底色 (Ambient)
                // 利用 URP 球谐函数 SampleSH(N) 获取动态环境漫反射，并叠加上环境遮蔽
                float3 ambientGI = 0.3 * albedo * occlusion;

                // 最终颜色合成
                float3 finalColor = directLight + ambientGI;

                return float4(finalColor, mainTex.a);
            }
            ENDHLSL
        }
    }
}