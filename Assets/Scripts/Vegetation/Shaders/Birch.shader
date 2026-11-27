Shader "Unlimited/Vegetation/Birch"
{
 Properties {
 _Color("Base color", Color) = (0.35,0.22,0.12,1)
 _Foliage("Foliage", Float) = 0
 _Wind("Wind amplitude", Float) = 0.12
 _LeafTiles("Leaf pattern tiles", Float) = 3
 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
 Cull Off
 Pass {
 Tags { "LightMode"="UniversalForward" }
 HLSLPROGRAM
 #pragma target 3.0
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 CBUFFER_START(UnityPerMaterial)
 float4 _Color; float _Foliage, _Wind, _LeafTiles;
 CBUFFER_END
 struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct V { float4 pos:SV_POSITION; float3 local:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; float3 world:TEXCOORD3; };
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 V vert(A a){UNITY_SETUP_INSTANCE_ID(a); V o; o.local=a.positionOS.xyz; float3 w=TransformObjectToWorld(o.local); w.x += _Foliage*_Wind*sin(_Time.y*2+w.x*.7+w.z*.4); w.z += _Foliage*_Wind*.5*cos(_Time.y*1.7+w.z); o.world=w; o.pos=TransformWorldToHClip(w);o.normal=TransformObjectToWorldNormal(a.normalOS);o.uv=a.uv;return o;}
 // Continuous 3D value noise: no UV seams or projection boundaries.
 float barkHash3(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float barkNoise3(float3 p){
 float3 q=floor(p),f=frac(p);f=f*f*(3-2*f);
 float a=lerp(barkHash3(q),barkHash3(q+float3(1,0,0)),f.x);
 float b=lerp(barkHash3(q+float3(0,1,0)),barkHash3(q+float3(1,1,0)),f.x);
 float c=lerp(barkHash3(q+float3(0,0,1)),barkHash3(q+float3(1,0,1)),f.x);
 float d=lerp(barkHash3(q+float3(0,1,1)),barkHash3(q+float3(1,1,1)),f.x);
 return lerp(lerp(a,b,f.y),lerp(c,d,f.y),f.z);
 }
 half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target {
 float3 n=normalize(i.normal)*IS_FRONT_VFACE(face,1,-1);
 float3 c; float transmission=0;
 if(_Foliage>.5){
 float2 tile=i.uv*_LeafTiles;float2 cell=floor(tile);float2 p=frac(tile)-.5;
 float a=(hash(cell)-.5)*1.1; p=mul(float2x2(cos(a),-sin(a),sin(a),cos(a)),p);
 float y=p.y/.46;float envelope=pow(saturate(1-y*y),.65);
 float lobes=.78+.22*cos((y+.12)*18.85);
 float width=.31*envelope*(.92+.08*cos(y*32));
 clip(min(width-abs(p.x),.46-abs(p.y)));
 float mid=1-smoothstep(.006,.017,abs(p.x));
 float side=1-smoothstep(.008,.022,abs(sin((p.y-abs(p.x)*.72)*47))*.13);
 side*=saturate(1-abs(p.x)/max(width,.01));
 float grain=noise(p*130);float mottled=noise(p*13+cell);
 c=lerp(float3(.20,.32,.025),float3(.48,.68,.095),saturate(.25+mottled*.5+hash(cell)*.25));
 c*=.82+.3*grain;c+=float3(.07,.09,.015)*(mid*.65+side*.35);
 transmission=.22;
 }else{
 // Continuous noise masks, no cell-edge clipping or hard grain.
 float3 p=i.local;
 float dist=distance(i.world,_WorldSpaceCameraPos);
 float broad=barkNoise3(p*float3(5,9,5)+19);
 float ribbon=barkNoise3(p*float3(9,42,9)+43);
 float scarField=barkNoise3(p*float3(7,24,7)+float3(17,31,53));
 float scarSignal=scarField+(ribbon-.5)*.28;
 float aa=max(.018,fwidth(scarSignal));
 float scar=smoothstep(.70-aa,.76+aa,scarSignal);
 c=lerp(float3(.46,.48,.46),float3(.87,.85,.79),smoothstep(.05,.92,broad));
 c*=.91+.17*ribbon;
 c=lerp(c,float3(.045,.038,.033),scar*.96);
 float detail=1-smoothstep(3,15,dist);
 [branch] if(dist<15){
 float3 fineP=p*float3(20,125,20)+71;
 float footprint=max(length(ddx(fineP)),length(ddy(fineP)));
 float fine=barkNoise3(fineP);
 float filtered=detail*(1-smoothstep(.35,1.25,footprint));
 float fissure=smoothstep(.72,.86,fine)*filtered;
 c=lerp(c,float3(.23,.21,.18),fissure*.72);
 c*=1+(fine-.5)*.20*filtered;
 }
 float peel=smoothstep(.48,.70,ribbon)*(1-smoothstep(.70,.88,ribbon));
 c+=float3(.075,.072,.065)*peel*(1-scar);
 float baseMask=1-smoothstep(.03,.70,p.y+(broad-.5)*.35);
 c=lerp(c,c*.48,baseMask*.8);
 c=saturate(c);
 }
 Light light=GetMainLight();float ndl=dot(n,light.direction);
 float3 ambient=max(SampleSH(n),float3(.12,.12,.12));
 float3 lit=c*(ambient+light.color*(saturate(ndl)*.85+transmission*saturate(-ndl)));
 return half4(lit,1);
 }
 ENDHLSL
 }
 }
}
