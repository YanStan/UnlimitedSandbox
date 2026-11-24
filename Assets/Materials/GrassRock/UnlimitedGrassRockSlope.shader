Shader "Unlimited/Grass Rock Slope Blend"
{
    Properties
    {
        _RockStart ("Rock Starts At Slope Degrees", Range(0,89)) = 30
        _RockEnd ("Full Rock At Slope Degrees", Range(1,90)) = 55
        _TransitionNoise ("Boundary Irregularity Degrees", Range(0,20)) = 4
        _TransitionScale ("Boundary Noise Scale", Range(0.001,1)) = 0.035
        _WorldScale ("Rock World Pattern Scale", Float) = 1
        _RockBrightness ("Rock Brightness", Range(0.1,3)) = 1
        _FarStart ("Full Detail Until (meters)", Range(10,1000)) = 120
        _FarEnd ("Simplified Beyond (meters)", Range(20,2000)) = 180
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _RockStart;
                float _RockEnd;
                float _TransitionNoise;
                float _TransitionScale;
                float _WorldScale;
                float _RockBrightness;
                float _FarStart;
                float _FarEnd;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 worldPos : TEXCOORD0; float3 worldNormal : TEXCOORD1; half fogFactor : TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS);
                o.positionCS = p.positionCS;
                o.worldPos = p.positionWS;
                o.worldNormal = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            float hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yxz + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float noise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);

                f = f * f * (3.0 - 2.0 * f);

                float n000 = hash31(i + float3(0,0,0));
                float n100 = hash31(i + float3(1,0,0));
                float n010 = hash31(i + float3(0,1,0));
                float n110 = hash31(i + float3(1,1,0));
                float n001 = hash31(i + float3(0,0,1));
                float n101 = hash31(i + float3(1,0,1));
                float n011 = hash31(i + float3(0,1,1));
                float n111 = hash31(i + float3(1,1,1));

                float x00 = lerp(n000, n100, f.x);
                float x10 = lerp(n010, n110, f.x);
                float x01 = lerp(n001, n101, f.x);
                float x11 = lerp(n011, n111, f.x);

                return lerp(
                    lerp(x00, x10, f.y),
                    lerp(x01, x11, f.y),
                    f.z
                );
            }


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


            float3 GrassColor(float3 P)
            {
                float macro = noise3(P * 2.8 + float3(4.1,1.7,8.2));
                float mid = noise3(P * 13.0 + float3(7.3,2.1,5.8));
                float fine = noise3(P * 65.0 + float3(2.7,9.1,3.4));
                float micro = noise3(P * 150.0 + float3(8.2,4.3,1.6));
                // Original Babylon colors.
                float3 darkGreen = float3(0.105, 0.155, 0.045);
                float3 baseGreen = float3(0.245, 0.335, 0.095);
                float3 lightGreen = float3(0.39, 0.46, 0.15);
                float3 yellowGreen = float3(0.48, 0.49, 0.19);

                float broad = clamp(
                    (macro - 0.28) * 1.35,
                    0.0,
                    1.0
                );

                float variation =
                    (macro - 0.5) * 0.34 +
                    (mid - 0.5) * 0.23 +
                    (fine - 0.5) * 0.17;

                float3 color = lerp(
                    darkGreen,
                    baseGreen,
                    0.58 + broad * 0.25
                );

                color = lerp(
                    color,
                    lightGreen,
                    clamp((mid - 0.60) * 0.65, 0.0, 0.35)
                );

                color = lerp(
                    color,
                    yellowGreen,
                    clamp((macro - 0.76) * 0.55, 0.0, 0.16)
                );

                color *= 0.88 + variation;
                color *= 0.91 + micro * 0.18;

                // Fine irregular grass-like flecks.
                float fleck = noise3(
                    P * 105.0 + float3(17.2, 3.8, 11.4)
                );

                float fleck2 = noise3(
                    P * 210.0 + float3(3.2, 14.8, 6.1)
                );

                float dark = 1.0 - smoothstep(
                    0.16, 0.34, fleck
                );

                float bright = smoothstep(
                    0.72, 0.88, fleck2
                );

                color = lerp(
                    color,
                    color * float3(0.48, 0.53, 0.30),
                    dark * 0.28
                );

                color = lerp(
                    color,
                    float3(0.53, 0.57, 0.25),
                    bright * 0.17
                );


                return color;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 P = input.worldPos;
                float3 N = normalize(input.worldNormal);
                float3 delta = GetCameraPositionWS() - P;
                float ds = dot(delta, delta);
                float endDistance = max(_FarEnd, _FarStart + 1.0);
                float fade = smoothstep(_FarStart * _FarStart, endDistance * endDistance, ds);
                float startAngle = min(_RockStart, _RockEnd - 0.1);
                float endAngle = max(startAngle + 0.1, _RockEnd);
                float w = 1.0 - smoothstep(cos(radians(endAngle)), cos(radians(startAngle)), N.y);
                Light ml = GetMainLight();
                float3 L = normalize(ml.direction);
                float diffuse = saturate(dot(N,L));
                float3 farLit = lerp(float3(0.245,0.335,0.095), float3(0.33,0.32,0.29)*_RockBrightness,w)*(0.38+0.72*diffuse);
                UNITY_BRANCH
                if (fade >= 1.0) return half4(MixFog(farLit,input.fogFactor),1);
                float margin = max(0.0,_TransitionNoise);
                UNITY_BRANCH
                if (N.y >= cos(radians(max(0.0,startAngle-margin)))) w=0.0;
                else if (N.y <= cos(radians(min(90.0,endAngle+margin)))) w=1.0;
                else {
                    float slope = degrees(acos(clamp(N.y,-1.0,1.0)));
                    float irregularity = (noise3(P*_TransitionScale+float3(11.3,2.7,7.1))-0.5)*2.0*margin;
                    w=smoothstep(startAngle,endAngle,slope+irregularity);
                }
                float3 H = SafeNormalize(L + SafeNormalize(delta));
                float nh = saturate(dot(N,H));
                float3 grassLit = 0;
                float3 rockLit = 0;
                UNITY_BRANCH
                if (w < 1.0) {
                    grassLit = GrassColor(P) * ((0.22+0.18*saturate(N.y)).xxx + ml.color*(diffuse*1.15)) + (pow(nh,48.0)*0.035).xxx;
                }
                UNITY_BRANCH
                if (w > 0.0) {
                    rockLit = RockColor(P)*_RockBrightness*(0.32+0.68*diffuse)*(0.32+0.68*saturate(N.y)) + (pow(nh,75.0)*0.009).xxx;
                }
                return half4(MixFog(lerp(lerp(grassLit,rockLit,w),farLit,fade),input.fogFactor),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
