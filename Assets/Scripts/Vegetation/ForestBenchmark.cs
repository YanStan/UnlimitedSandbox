using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;
namespace Unlimited.Vegetation {
public sealed class ForestBenchmark : MonoBehaviour {
 internal static bool Running, FreezeLod;
 public float warmup=4, duration=8, startupDelay=90;
 public bool autoRun=false;
 readonly List<Renderer> rs=new List<Renderer>();
 readonly List<Collider> cs=new List<Collider>();
 readonly List<bool> re=new List<bool>(), ce=new List<bool>(), imp=new List<bool>(), leaf=new List<bool>();
 readonly List<ShadowCastingMode> shadows=new List<ShadowCastingMode>();
 readonly List<Material[]> materials=new List<Material[]>();
 Material simple; Coroutine run; readonly Dictionary<Material,Material> replacements=new Dictionary<Material,Material>();
 static readonly string[] names={"Baseline","OriginalBark","Baseline"};
 static readonly string[] counters={"Main Thread","Render Thread","Draw Calls Count","SetPass Calls Count","Batches Count","Triangles Count","GC Allocated In Frame","Physics.Simulate"};
 static readonly ProfilerCategory[] categories={ProfilerCategory.Internal,ProfilerCategory.Internal,ProfilerCategory.Render,ProfilerCategory.Render,ProfilerCategory.Render,ProfilerCategory.Render,ProfilerCategory.Memory,ProfilerCategory.Physics};
 IEnumerator Start(){if(autoRun){yield return new WaitForSecondsRealtime(startupDelay);Begin();}}
 [ContextMenu("Run forest benchmark") ] public void Begin(){if(run==null&&!Running&&Application.isPlaying)run=StartCoroutine(Test());}
 void Capture(){foreach(var t in TreeDistanceLod.Active){if(!t)continue;var d=t.GetComponent<TreeDamagePreview>();if(!d||!d.forestInstance)continue;foreach(var r in t.GetComponentsInChildren<Renderer>(true)){rs.Add(r);re.Add(r.enabled);imp.Add(r==t.impostor);leaf.Add(d.leaves&&(r.transform==d.leaves.transform||r.transform.IsChildOf(d.leaves.transform)));shadows.Add(r.shadowCastingMode);materials.Add(r.sharedMaterials);}foreach(var c in t.GetComponentsInChildren<Collider>(true)){cs.Add(c);ce.Add(c.enabled);}}}
 void Restore(){for(int i=0;i<rs.Count;i++)if(rs[i]){rs[i].enabled=re[i];rs[i].shadowCastingMode=shadows[i];rs[i].sharedMaterials=materials[i];}for(int i=0;i<cs.Count;i++)if(cs[i])cs[i].enabled=ce[i];}
 void Apply(int mode){Restore();FreezeLod=true;string name=names[mode];
 var shader=Shader.Find("Unlimited/Diagnostics/"+name);
 if(name!="Baseline"&&name!="NoImpostors"&&name!="NoForestRendering"&&(!shader||!shader.isSupported)) Debug.LogWarning("[ForestBenchmark] Unsupported variant: "+name);
 foreach(var m in replacements.Values)if(m)Destroy(m);replacements.Clear();
 for(int i=0;i<rs.Count;i++){var r=rs[i];if(!r||!re[i])continue;
 if(name=="NoForestRendering"||(name=="NoImpostors"&&imp[i])){r.enabled=false;continue;}
 if(!shader||!shader.isSupported||imp[i])continue;
 var a=(Material[])materials[i].Clone();bool changed=false;
 for(int j=0;j<a.Length;j++){var original=a[j];if(!original||original.shader.name!="Unlimited/Vegetation/ProceduralTree")continue;
 bool foliage=original.HasProperty("_Foliage")&&original.GetFloat("_Foliage")>.5f;
 if(foliage!=(name=="FlatLeaves"))continue;
 if(!replacements.TryGetValue(original,out var m)){m=new Material(original);m.shader=shader;replacements.Add(original,m);}a[j]=m;changed=true;}
 if(changed)r.sharedMaterials=a;
 }Debug.Log($"[ForestBenchmark] Phase {mode}: {name}, replacedMaterials={replacements.Count}, terrains={Terrain.activeTerrains.Length}");}
 IEnumerator Test(){Running=true;FreezeLod=true;Capture();var rec=new ProfilerRecorder[counters.Length];var cam=Camera.main;var start=cam?cam.transform.position:Vector3.zero;var rot=cam?cam.transform.rotation:Quaternion.identity;
 try{for(int i=0;i<rec.Length;i++){try{rec[i]=ProfilerRecorder.StartNew(categories[i],counters[i],1);}catch(Exception){}}
 Debug.Log($"[ForestBenchmark] START trees/renderers={rs.Count}; keep camera still. Forest spawning paused for all tests. V25 optimized / original bark / optimized; foliage unchanged.");
 for(int mode=0;mode<names.Length;mode++){Apply(mode);yield return new WaitForSecondsRealtime(warmup);var times=new List<float>();var sums=new double[rec.Length];var counts=new int[rec.Length];double gpu=0;int gpuN=0;var timings=new FrameTiming[1];float end=Time.realtimeSinceStartup+duration;bool moved=false;
 while(Time.realtimeSinceStartup<end){FrameTimingManager.CaptureFrameTimings();yield return null;times.Add(Time.unscaledDeltaTime*1000);for(int i=0;i<rec.Length;i++)if(rec[i].Valid&&rec[i].Count>0){sums[i]+=rec[i].LastValue;counts[i]++;}if(FrameTimingManager.GetLatestTimings(1,timings)>0&&timings[0].gpuFrameTime>0){gpu+=timings[0].gpuFrameTime;gpuN++;}if(cam&&(Vector3.Distance(start,cam.transform.position)>.1f||Quaternion.Angle(rot,cam.transform.rotation)>1))moved=true;}
 times.Sort();if(times.Count==0)continue;double sum=0;foreach(float t in times)sum+=t;var msg=new System.Text.StringBuilder($"[ForestBenchmark] {names[mode]} FPS={1000*times.Count/Math.Max(.001,sum):F2} avg={sum/times.Count:F2}ms P95={times[Mathf.CeilToInt(times.Count*.95f)-1]:F2} P99={times[Mathf.CeilToInt(times.Count*.99f)-1]:F2} GPU={(gpuN>0?(gpu/gpuN).ToString("F2")+"ms":"N/A")} moved={moved}");for(int i=0;i<rec.Length;i++){double divisor=(i==0||i==1||i==7)?1000000:1;msg.Append($"; {counters[i]}={(counts[i]>0?(sums[i]/counts[i]/divisor).ToString("F2"):"N/A")}");}Debug.Log(msg.ToString());}
 }finally{for(int i=0;i<rec.Length;i++)if(rec[i].Valid)rec[i].Dispose();Cleanup();}Debug.Log("[ForestBenchmark] DONE");run=null;}
 void Cleanup(){Restore();rs.Clear();cs.Clear();re.Clear();ce.Clear();imp.Clear();leaf.Clear();shadows.Clear();materials.Clear();foreach(var m in replacements.Values)if(m)Destroy(m);replacements.Clear();if(simple)Destroy(simple);Running=FreezeLod=false;}
 void OnDisable(){if(run!=null){StopCoroutine(run);run=null;}if(Running)Cleanup();}
}
}
