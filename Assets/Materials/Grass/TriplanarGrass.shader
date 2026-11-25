
Shader "Unlimited/TriplanarGrass"
{
    Properties
    {
        [HideInInspector] _HybridHoleEnabled ("Hybrid holes", Float) = 0
        [HideInInspector] _HybridHoleRect ("Hybrid rect", Vector) = (0,0,1,1)
        [HideInInspector] _HybridHoleResolution ("Hybrid resolution", Float) = 1
        [HideInInspector] _HybridHoleTexture ("Hybrid holes texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Grass"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            Blend Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_HybridHoleTexture);
            CBUFFER_START(UnityPerMaterial)
                float _HybridHoleEnabled;
                float4 _HybridHoleRect;
                float _HybridHoleResolution;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

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

            Varyings Vert(Attributes input)
            {
                Varyings output;

                VertexPositionInputs pos =
                    GetVertexPositionInputs(input.positionOS);

                output.positionCS = pos.positionCS;
                output.worldPos = pos.positionWS;

                output.worldNormal =
                    TransformObjectToWorldNormal(input.normalOS);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 N = normalize(input.worldNormal);
                float3 P = input.worldPos;
                if (_HybridHoleEnabled > 0.5) {
                    float2 uv = (P.xz - _HybridHoleRect.xy) / _HybridHoleRect.zw;
                    int2 pixel = (int2)clamp(floor(uv * _HybridHoleResolution), 0, _HybridHoleResolution - 1);
                    clip(LOAD_TEXTURE2D(_HybridHoleTexture, pixel).r - 0.5);
                }

                // Triplanar weights.
                float3 weights = pow(abs(N), float3(4.0, 4.0, 4.0));

                weights /= max(
                    weights.x + weights.y + weights.z,
                    0.0001
                );

                // MACRO
                float macroX = noise3(P * 2.8 + float3(4.1, 1.7, 8.2));
                float macroY = noise3(P * 2.8 + float3(4.1, 1.7, 8.2));
                float macroZ = noise3(P * 2.8 + float3(4.1, 1.7, 8.2));

                float macro =
                    macroX * weights.x +
                    macroY * weights.y +
                    macroZ * weights.z;

                // MID
                float midX = noise3(P * 13.0 + float3(7.3, 2.1, 5.8));
                float midY = noise3(P * 13.0 + float3(7.3, 2.1, 5.8));
                float midZ = noise3(P * 13.0 + float3(7.3, 2.1, 5.8));

                float mid =
                    midX * weights.x +
                    midY * weights.y +
                    midZ * weights.z;

                // FINE
                float fineX = noise3(P * 65.0 + float3(2.7, 9.1, 3.4));
                float fineY = noise3(P * 65.0 + float3(2.7, 9.1, 3.4));
                float fineZ = noise3(P * 65.0 + float3(2.7, 9.1, 3.4));

                float fine =
                    fineX * weights.x +
                    fineY * weights.y +
                    fineZ * weights.z;

                // MICRO
                float microX = noise3(P * 150.0 + float3(8.2, 4.3, 1.6));
                float microY = noise3(P * 150.0 + float3(8.2, 4.3, 1.6));
                float microZ = noise3(P * 150.0 + float3(8.2, 4.3, 1.6));

                float micro =
                    microX * weights.x +
                    microY * weights.y +
                    microZ * weights.z;

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

                // Real directional light from URP.
                Light mainLight = GetMainLight();

                float3 L = normalize(mainLight.direction);

                // Stronger slope-dependent lighting.
                float diffuse = max(dot(N, L), 0.0);

                // Более выраженное объёмное освещение.
                float ambient =
                0.22 + 0.18 * max(N.y, 0.0);

                // Усиленное прямое освещение.
                float3 lighting =
                ambient.xxx +
                mainLight.color * (diffuse * 1.15);

                color *= lighting;

                // Restrained matte highlight.
                float3 V = normalize(
                    GetCameraPositionWS() - P
                );

                float3 H = normalize(L + V);

                float spec = pow(
                    max(dot(N, H), 0.0),
                    48.0
                ) * 0.035;

                color += spec.xxx;

                return half4(color, 1.0);
            }

            ENDHLSL
        }

        // Terrain can cast shadows onto other objects.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
    }
}

