Shader "Unlimited/Rock V13"
{
    Properties
    {
        _WorldScale ("World Pattern Scale", Float) = 1
        _Brightness ("Brightness", Range(0.1, 3)) = 1
        _BumpStrength ("Surface Bump (Experimental)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _WorldScale;
                float _Brightness;
                float _BumpStrength;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            // Direct port of Babylon Rock Material V13's hash and 3D value noise.
            float RockHash(float3 p)
            {
                p = frac(p * 0.3183099 + float3(0.1, 0.2, 0.3));
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float RockNoise(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = RockHash(i + float3(0,0,0));
                float n100 = RockHash(i + float3(1,0,0));
                float n010 = RockHash(i + float3(0,1,0));
                float n110 = RockHash(i + float3(1,1,0));
                float n001 = RockHash(i + float3(0,0,1));
                float n101 = RockHash(i + float3(1,0,1));
                float n011 = RockHash(i + float3(0,1,1));
                float n111 = RockHash(i + float3(1,1,1));
                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);
                return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
            }

            float3 RockColor(float3 worldPos)
            {
                float3 p = worldPos * (4.0 * _WorldScale);
                float3 warp = float3(
                    RockNoise(p * 0.8 + float3(2.1, 7.3, 1.4)),
                    RockNoise(p * 0.8 + float3(8.2, 1.6, 4.7)),
                    RockNoise(p * 0.8 + float3(3.5, 5.1, 9.2)));
                float3 q = p + (warp - 0.5) * 0.44;
                float grainLarge = RockNoise(q * 9.0);
                float grainMedium = RockNoise(q * 23.0);
                float grainSmall = RockNoise(q * 53.0);
                float grainMicro = RockNoise(q * 105.0);
                float flecks = RockNoise(q * 39.0 + float3(5.2, 2.4, 8.1));
                float quartz = RockNoise(q * 67.0 + float3(12.1, 4.7, 2.8));
                float darkFlecks = 1.0 - smoothstep(0.12, 0.31, flecks);
                float brightFlecks = smoothstep(0.69, 0.87, flecks);
                float quartzSpots = smoothstep(0.77, 0.91, quartz);
                float3 charcoal = float3(0.19, 0.19, 0.18);
                float3 mediumGray = float3(0.34, 0.33, 0.30);
                float3 warmGray = float3(0.43, 0.405, 0.36);
                float3 paleGrain = float3(0.62, 0.59, 0.52);
                float3 color = lerp(charcoal, mediumGray, grainLarge);
                color = lerp(color, warmGray, grainMedium * 0.38);
                float3 macroP = worldPos * (2.8 * _WorldScale);
                float macroA = RockNoise(macroP + float3(3.7, 8.2, 1.4));
                float macroB = RockNoise(worldPos * (5.2 * _WorldScale) + float3(8.1, 2.6, 5.3));
                float macroC = RockNoise(worldPos * (8.0 * _WorldScale) + float3(1.9, 6.4, 9.1));
                float mineralA = RockNoise(worldPos * (7.0 * _WorldScale) + float3(4.3, 9.1, 2.7));
                float mineralB = RockNoise(worldPos * (11.0 * _WorldScale) + float3(7.4, 2.8, 6.1));
                float macroTone = (macroA - 0.5) * 0.105 + (macroB - 0.5) * 0.045 + (macroC - 0.5) * 0.018;
                float mineralTone = (mineralA - 0.5) * 0.055 + (mineralB - 0.5) * 0.025;
                color += float3(macroTone * 0.90 + mineralTone,
                                macroTone * 0.82 + mineralTone * 0.94,
                                macroTone * 0.68 + mineralTone * 0.82);
                color *= 0.79 + grainSmall * 0.43;
                color *= 0.91 + grainMicro * 0.18;
                color = lerp(color, float3(0.105, 0.105, 0.10), darkFlecks * 0.48);
                color = lerp(color, float3(0.56, 0.54, 0.48), brightFlecks * 0.38);
                color = lerp(color, paleGrain, quartzSpots * 0.42);
                return color;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 color = RockColor(i.positionWS) * _Brightness;
                float3 N = normalize(i.normalWS);
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float3 H = normalize(L + V);
                float diffuse = max(dot(N, L), 0.0);
                float ambientLight = 0.32 + 0.68 * max(N.y, 0.0);
                float specular = pow(max(dot(N, H), 0.0), 75.0) * 0.009;
                // Babylon V13 lighting reproduced using Unity's main-light direction.
                float3 finalColor = color * (0.32 + 0.68 * diffuse) * ambientLight + specular.xxx;
                finalColor = MixFog(finalColor, i.fogFactor);
                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
