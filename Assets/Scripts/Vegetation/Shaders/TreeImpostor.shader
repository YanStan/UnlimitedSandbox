Shader "Unlimited/Vegetation/TreeImpostor" {
 Properties {

 _MainTex("Atlas",2D)="white"{} _Frame("Frame",Float)=0 }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" } Cull Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
 CBUFFER_START(UnityPerMaterial)

 float _Frame;
 CBUFFER_END
 struct A {float4 pos:POSITION;float2 uv:TEXCOORD0;}; struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float2 uvNext:TEXCOORD1;float mixFrame:TEXCOORD2;};
 V vert(A a){V o;o.pos=TransformObjectToHClip(a.pos.xyz);o.uv=(a.uv*0.984+0.008+float2(fmod(floor(_Frame),4),floor(floor(_Frame)/4)))/float2(4,2);float next=fmod(floor(_Frame)+1,8);o.uvNext=(a.uv*.984+.008+float2(fmod(next,4),floor(next/4)))/float2(4,2);o.mixFrame=frac(_Frame);return o;}
 half4 frag(V i):SV_Target{half4 a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);half4 b=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uvNext);half alpha=lerp(a.a,b.a,i.mixFrame);half3 rgb=lerp(a.rgb*a.a,b.rgb*b.a,i.mixFrame)/max(alpha,.001);half4 c=half4(rgb,alpha);clip(c.a-.45);return half4(c.rgb,1);}
 ENDHLSL } }
}
