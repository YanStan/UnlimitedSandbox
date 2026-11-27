Shader "Unlimited/Vegetation/FarForest" {
 Properties { _MainTex("Atlas",2D)="white"{} }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" } Cull Off
 Pass { HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
 CBUFFER_START(UnityPerMaterial)
 float4 _ForestCenter; float _ForestRadius;
 CBUFFER_END
 struct A {float4 pos:POSITION;float2 uv:TEXCOORD0;float4 info:TEXCOORD1;}; struct V {float4 pos:SV_POSITION;float2 uv:TEXCOORD0;float visible:TEXCOORD1;float2 uvNext:TEXCOORD2;float mixFrame:TEXCOORD3;};
 V vert(A a){V o;float2 d=_WorldSpaceCameraPos.xz-a.pos.xz;float len=max(length(d),.001);float2 right=float2(d.y,-d.x)/len; if(len<.002)right=float2(1,0);float3 p=a.pos.xyz; p.xz+=right*(1-2*a.uv.x)*a.info.x;p.y+=(2*a.uv.y-1)*a.info.x;float angle=atan2(d.x,d.y)*57.2957795-a.info.y;float continuous=fmod(fmod(angle/45,8)+8,8);float frame=floor(continuous);float next=fmod(frame+1,8);o.mixFrame=frac(continuous);o.uvNext=(a.uv*.984+.008+float2(fmod(next,4),floor(next/4)))/float2(4,2);o.pos=TransformWorldToHClip(p);o.uv=(a.uv*.984+.008+float2(fmod(frame,4),floor(frame/4)))/float2(4,2);o.visible=_ForestRadius-length(a.info.zw-_ForestCenter.xz);return o;}
 half4 frag(V i):SV_Target{half4 a=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv);half4 b=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uvNext);clip(i.visible);half alpha=lerp(a.a,b.a,i.mixFrame);half3 rgb=lerp(a.rgb*a.a,b.rgb*b.a,i.mixFrame)/max(alpha,.001);half4 c=half4(rgb,alpha);clip(c.a-.45);return half4(c.rgb,1);}
 ENDHLSL } }
}
