Shader "Unlimited/Vegetation/ProceduralTree"
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
 struct V { float4 pos:SV_POSITION; float3 local:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; float3 world:TEXCOORD3; nointerpolation float barkDistance:TEXCOORD4; };
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
 V vert(A a){UNITY_SETUP_INSTANCE_ID(a); V o; o.local=a.positionOS.xyz; o.barkDistance=distance(GetCameraPositionWS(),TransformObjectToWorld(float3(0,0,0))); float3 w=TransformObjectToWorld(o.local); w.x += _Foliage*_Wind*sin(_Time.y*2+w.x*.7+w.z*.4); w.z += _Foliage*_Wind*.5*cos(_Time.y*1.7+w.z); o.world=w; o.pos=TransformWorldToHClip(w);o.normal=TransformObjectToWorldNormal(a.normalOS);o.uv=a.uv;return o;}
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
 // Elongated cellular plates. Distances remain continuous across cell boundaries.
 float3 barkCells(float3 p){
 float3 baseCell=floor(p), f=frac(p);
 float d1=100, d2=100, tone=0;
 [loop] for(int z=-1;z<=1;z++){
 [loop] for(int y=-1;y<=1;y++){
 [loop] for(int x=-1;x<=1;x++){
 float3 offset=float3(x,y,z), id=baseCell+offset;
 float3 jitter=float3(barkHash3(id+11),barkHash3(id+37),barkHash3(id+73));
 float3 delta=offset+.2+.6*jitter-f;
 float d=dot(delta,delta);
 if(d<d1){d2=d1;d1=d;tone=barkHash3(id+97);}
 else {d2=min(d2,d);}
 }}}
 return float3(sqrt(d1),sqrt(d2)-sqrt(d1),tone);
 }
 half4 frag(V i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC):SV_Target {
 float3 n=normalize(i.normal)*IS_FRONT_VFACE(face,1,-1);
 float3 c; float transmission=0;
 if(_Foliage>.5){
 float2 tile=i.uv*_LeafTiles;float2 cell=floor(tile);float2 p=frac(tile)-.5;
 float a=(hash(cell)-.5)*1.1; p=mul(float2x2(cos(a),-sin(a),sin(a),cos(a)),p);
 float y=p.y/.46;float envelope=pow(saturate(1-y*y),.65);
 float lobes=.78+.22*cos((y+.12)*18.85);
 float width=.34*envelope*lobes;
 clip(min(width-abs(p.x),.46-abs(p.y)));
 float mid=1-smoothstep(.006,.017,abs(p.x));
 float side=1-smoothstep(.008,.022,abs(sin((p.y-abs(p.x)*.72)*47))*.13);
 side*=saturate(1-abs(p.x)/max(width,.01));
 float grain=noise(p*130);float mottled=noise(p*13+cell);
 c=lerp(float3(.018,.045,.006),float3(.085,.145,.018),saturate(.25+mottled*.5+hash(cell)*.25));
 c*=.82+.3*grain;c+=float3(.035,.045,.009)*(mid*.65+side*.35);
 transmission=.22;
 }else{
 [branch] if(i.barkDistance < 10.0){
 // Continuous object-space bark; all detail bands are footprint filtered.
 float3 p=i.local*float3(87,21,87);
 float footprint=max(length(ddx(p)),length(ddy(p)));
 float visible=1-smoothstep(.60,2.28,footprint);
 float3 warp=float3(barkNoise3(p*1.4+11),barkNoise3(p*1.4+29),barkNoise3(p*1.4+53));
 float3 warp2=float3(barkNoise3(p*3.1+13),barkNoise3(p*3.1+31),barkNoise3(p*3.1+57));
 float3 q=p+(warp-.5)*.65+(warp2-.5)*.19*(1-smoothstep(.72,2.10,footprint));
 float3 cells=barkCells(q);
 float aa=max(.009, footprint*.85);
 float broad=barkNoise3(p*.16+39);
 float medium=lerp(.5,barkNoise3(p*2.1+17),1-smoothstep(.48,1.68,footprint));
 float crackWidth=lerp(.022,.048,broad);
 float plate=smoothstep(max(0,crackWidth-aa),crackWidth+.045+aa,cells.y);
 float3 fp=p*float3(3.0,1.0,3.0);
 fp.xz+=(float2(barkNoise3(p*1.3+53),barkNoise3(p*1.3+71))-.5)*1.5;
 float ff=1-smoothstep(.96,3.90,max(length(ddx(fp)),length(ddy(fp))));
 float fibre=barkNoise3(fp+83);
 float3 gp=p*float3(2.0,1.5,2.0);
 float gf=1-smoothstep(1.08,4.2,max(length(ddx(gp)),length(ddy(gp))));
 float grain=barkNoise3(gp+101);
 float3 sp=p*float3(5,2,5);
 float sf=1-smoothstep(.84,3.30,max(length(ddx(sp)),length(ddy(sp))));
 float splinter=barkNoise3(sp+121);
 float interior=smoothstep(.035,.15,cells.y)*plate;
 float wood=(fibre-.5)*.24*ff+(grain-.5)*.19*gf+(splinter-.5)*.065*sf;
 [branch] if(i.barkDistance < 3.0){
 float relief=lerp(.55,plate*(.53+.13*medium+.12*broad)+interior*wood,visible);
 float3 dx=ddx(i.world),dy=ddy(i.world);
 float3 r1=cross(dy,n),r2=cross(n,dx);
 float det=dot(dx,r1);
 float3 grad=(r1*ddx(relief)+r2*ddy(relief))*sign(det)/max(abs(det),1e-8);
 float3 bump=grad*.0055;
 bump*=min(1.0,.28/max(length(bump),.0001));
 n=normalize(n-bump*visible);
 }
 float3 baseColor=lerp(float3(.105,.083,.064),float3(.225,.185,.143),saturate(broad*.6+medium*.4));
 baseColor*=lerp(1,.92+.16*cells.z,plate*visible);
 float3 fissure=baseColor*lerp(.40,.64,medium);
 c=lerp(baseColor,lerp(fissure,baseColor,plate),visible);
 c*=1+interior*visible*((fibre-.5)*.21*ff+(grain-.5)*.16*gf+(splinter-.5)*.05*sf);
 // Two enhanced color-only noise bands; no additional bump or cracks.
 float3 tintCoarseP=p*.075+float3(19,47,83);
 float tintCoarse=barkNoise3(tintCoarseP);
 float tintCoarseFade=1-smoothstep(.45,1.2,max(length(ddx(tintCoarseP)),length(ddy(tintCoarseP))));
 float3 tintFineP=p*.65+float3(71,23,109);
 float tintFine=barkNoise3(tintFineP);
 float tintFineFade=1-smoothstep(.35,.95,max(length(ddx(tintFineP)),length(ddy(tintFineP))));
 c*=1+(tintCoarse-.5)*float3(.30,.475,.625)*tintCoarseFade;
 c*=1-tintFine*float3(.30,.40,.475)*tintFineFade;
 // Fine brown granular albedo, across plates and fissures alike.
 float3 brownGrainP=i.local*float3(620,620,620)+float3(137,59,211);
 float brownGrainFootprint=max(length(ddx(brownGrainP)),length(ddy(brownGrainP)));
 float brownGrainFade=1-smoothstep(.25,.85,brownGrainFootprint);
 float brownGrain=barkNoise3(brownGrainP);
 c*=1+(brownGrain-.5)*float3(.32,.43,.56)*brownGrainFade;
 }else{
 float3 p=i.local*float3(87,21,87);
 float broad=barkNoise3(p*.16+39);
 c=lerp(float3(.105,.083,.064),float3(.225,.185,.143),broad*.6+.2);
 float tint=barkNoise3(p*.075+float3(19,47,83));
 c*=1+(tint-.5)*float3(.30,.475,.625);
 [branch] if(i.barkDistance < 32.0){
 float fine=barkNoise3(p*.65+float3(71,23,109));
 float3 f=p*.65;
 float fade=1-smoothstep(.35,.95,max(length(ddx(f)),length(ddy(f))));
 c*=1-fine*float3(.30,.40,.475)*fade;
 }
 }


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
