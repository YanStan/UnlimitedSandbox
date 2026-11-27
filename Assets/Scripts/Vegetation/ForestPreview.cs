using System.Collections.Generic;
using UnityEngine;
namespace Unlimited.Vegetation {
public sealed class ForestPreview : MonoBehaviour {
 public TreeDamagePreview template;
 public TreeDamagePreview[] mixedTemplates;
 internal readonly List<TreeDamagePreview> Species=new List<TreeDamagePreview>();
 internal readonly List<float> Bases=new List<float>();
 internal int SpeciesIndex(Vector2Int k)=>(int)(Hash(k.x,k.y,seed+913)% (uint)Species.Count);
 public int seed=12345;
 [Min(4)] public float spacing=4;
 [Range(0,625)] public float treesPerHectare=200;
 [Min(1)] public float radius=2000;
 [Range(0,90)] public float maxSlope=35;
 [Min(1)] public int spawnBudget=16;
 public float previewRadius=272; public int maxLiveTrees=700; public float colliderRadius=30;
 public float groundSinkDepth=.5f; public bool profileTrees=true;
 public float minimumScale=.8f, maximumScale=1.2f;
 readonly HashSet<Vector2Int> visited=new HashSet<Vector2Int>();
 readonly Dictionary<Vector2Int,float> retryAfter=new Dictionary<Vector2Int,float>();
 readonly List<Vector2Int> pending=new List<Vector2Int>();
 readonly Dictionary<Vector2Int,GameObject> live=new Dictionary<Vector2Int,GameObject>();
 readonly List<Vector2Int> remove=new List<Vector2Int>();
 readonly RaycastHit[] groundHits=new RaycastHit[64];
 readonly Dictionary<Vector2Int,Collider[]> colliderCache=new Dictionary<Vector2Int,Collider[]>();
 readonly List<Vector2Int> groundQueue=new List<Vector2Int>(); int groundCursor; float nextGroundPass; float startupTime;
 FarForest far; readonly HashSet<Vector2Int> removedTrees=new HashSet<Vector2Int>();
 internal bool SuppressFar(Vector2Int k){return removedTrees.Contains(k)||(live.TryGetValue(k,out var g)&&g);}
 internal float GridStep=>Mathf.Max(4,spacing)*2f*1.41421356f/Mathf.Sqrt(1.3f);
 internal Transform PlayerTransform=>player?player.transform:null;
 internal bool NearPending {get;private set;}
 Vector3 lastScanPosition; bool scanned;
 float DistanceSq(Vector2Int k, Vector3 p){float dx=k.x*GridStep-p.x,dz=k.y*GridStep-p.z;return dx*dx+dz*dz;}
 float baseY; int groundMissing; float nextCollider;
 PlayerController player; float nextScan; GameObject source; Transform staging; float nextReport; int spawned, rejected, missing;
 void Start(){radius=Mathf.Max(radius,2000f);if(!template){foreach(var t in FindObjectsByType<TreeDamagePreview>(FindObjectsInactive.Include)){if(t.gameObject.scene==gameObject.scene && !t.forestInstance && t.wood && t.leaves && t.GetComponent<TreeDistanceLod>()){template=t;break;}}} if(!template){Debug.LogError("[Vegetation] No baked tree template found.");enabled=false;return;} source=template.gameObject;startupTime=Time.unscaledTime;gameObject.AddComponent<ForestBenchmark>().autoRun=false;nextGroundPass=startupTime+3;
 Species.Add(template); if(mixedTemplates!=null)foreach(var t in mixedTemplates)if(t && t.wood && t.leaves && t.GetComponent<TreeDistanceLod>() && t.GetComponent<TreeDistanceLod>().impostor && !Species.Contains(t))Species.Add(t);
 foreach(var t in Species)Bases.Add(MeasureBase(t));
 baseY=Bases[0];
 var holder=new GameObject("Inactive tree staging"); holder.transform.SetParent(transform,false); holder.SetActive(false); staging=holder.transform; far=gameObject.AddComponent<FarForest>();far.Initialize(this); Debug.Log("[Forest] Species count: "+Species.Count);}
 float MeasureBase(TreeDamagePreview tree){float baseY=float.PositiveInfinity;
 foreach(var f in tree.wood.GetComponentsInChildren<MeshFilter>(true)){
 if(!f.sharedMesh)continue;
 var m=tree.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
 if(f.sharedMesh.isReadable){foreach(var v in f.sharedMesh.vertices)baseY=Mathf.Min(baseY,m.MultiplyPoint3x4(v).y);}
 else {var b=f.sharedMesh.bounds;for(int i=0;i<8;i++)baseY=Mathf.Min(baseY,m.MultiplyPoint3x4(b.center+Vector3.Scale(b.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1))).y);}
 } if(float.IsInfinity(baseY))baseY=0;
 return baseY;}
 internal static uint Hash(int x,int z,int seed){unchecked{uint h=(uint)x*374761393u+(uint)z*668265263u+(uint)seed*2246822519u;h=(h^(h>>13))*1274126177u;return h^(h>>16);}}
 internal static float Unit(uint h){return (h&0xffffff)/16777216f;}
 void Update(){if(ForestBenchmark.Running)return;using(var sample=ForestMetrics.Measure(0)){UpdateForest();} if(profileTrees)ReportProfile();}
 void UpdateForest(){if(!player)player=FindAnyObjectByType<PlayerController>(); if(!source)return; if(!player){if(Time.unscaledTime>=nextReport){nextReport=Time.unscaledTime+5;Debug.LogWarning("[Forest] Waiting for PlayerController");}return;} var pos=player.transform.position;float step=GridStep;
 float range=Mathf.Max(272,previewRadius);
 using(var sample=ForestMetrics.Measure(1)){if(Time.unscaledTime>=nextCollider){nextCollider=Time.unscaledTime+.25f;foreach(var pair in live){var go=pair.Value;if(!go)continue;var d=go.GetComponent<TreeDamagePreview>();if(d.State>=2)continue;bool near=(go.transform.position-pos).sqrMagnitude<colliderRadius*colliderRadius;if(!colliderCache.TryGetValue(pair.Key,out var cached))continue;foreach(var c in cached)if(c && c.enabled!=near)c.enabled=near;}}}
 var terrains=Terrain.activeTerrains;
 // Recheck early placements after streamed terrain settles, in small batches.
 if(Time.unscaledTime>=nextGroundPass && Time.unscaledTime<startupTime+60 && groundCursor>=groundQueue.Count){nextGroundPass=Time.unscaledTime+3;groundQueue.Clear();groundQueue.AddRange(live.Keys);groundCursor=0;}
 for(int j=0;j<8 && groundCursor<groundQueue.Count;j++){
 var key=groundQueue[groundCursor++];if(!live.TryGetValue(key,out var tree)||!tree)continue;
 var damage=tree.GetComponent<TreeDamagePreview>();if(!damage||damage.State!=0)continue;
 float baseY=Bases[SpeciesIndex(key)];var q=tree.transform.position;float expected=q.y+baseY*tree.transform.localScale.y-damage.groundOffset+groundSinkDepth+.03f;
 if(TryGround(q.x,q.z,expected,out float corrected,out _)){q.y=corrected-baseY*tree.transform.localScale.y+damage.groundOffset-groundSinkDepth-.03f;if(Mathf.Abs(q.y-tree.transform.position.y)>.05f)tree.transform.position=q;}
 }

 using(var scan=ForestMetrics.Measure(2)){if(!scanned || Time.unscaledTime>=nextScan || (pos-lastScanPosition).sqrMagnitude>32*32){scanned=true;lastScanPosition=pos;nextScan=Time.unscaledTime+.2f;pending.Clear();remove.Clear();if(retryAfter.Count>4096)retryAfter.Clear();using(var unload=ForestMetrics.Measure(3)){foreach(var pair in live){if(!pair.Value){removedTrees.Add(pair.Key);remove.Add(pair.Key);continue;}var delta=pair.Value.transform.position-pos;if(delta.x*delta.x+delta.z*delta.z>(range+16)*(range+16)){if(pair.Value.GetComponent<TreeDamagePreview>().State!=0)continue;Destroy(pair.Value);remove.Add(pair.Key);visited.Remove(pair.Key);}}foreach(var k in remove){live.Remove(k);colliderCache.Remove(k);if(far)far.Dirty(k);}}
 int r=Mathf.CeilToInt(range/step);int cx=Mathf.FloorToInt(pos.x/step),cz=Mathf.FloorToInt(pos.z/step);
 // Scan only loaded terrain rectangles, not the entire world.
 var queued=new HashSet<Vector2Int>();
 foreach(var t in terrains){if(!t||!t.drawHeightmap||!t.terrainData)continue;var a=t.GetPosition();var s=t.terrainData.size;
 for(int z=Mathf.Max(cz-r,Mathf.CeilToInt(a.z/step));z<=Mathf.Min(cz+r,Mathf.FloorToInt((a.z+s.z)/step));z++)for(int x=Mathf.Max(cx-r,Mathf.CeilToInt(a.x/step));x<=Mathf.Min(cx+r,Mathf.FloorToInt((a.x+s.x)/step));x++){
 var k=new Vector2Int(x,z);if(visited.Contains(k)||!queued.Add(k))continue;if(retryAfter.TryGetValue(k,out float retry)&&Time.unscaledTime<retry)continue;float dx=x*step-pos.x,dz=z*step-pos.z;if(dx*dx+dz*dz>range*range)continue;
 if(Unit(Hash(x,z,seed))>=Mathf.Min(1,treesPerHectare*.1625f*step*step/10000f))continue;pending.Add(k);}}
 pending.Sort((a,b)=>((new Vector2(a.x*step-pos.x,a.y*step-pos.z)).sqrMagnitude).CompareTo((new Vector2(b.x*step-pos.x,b.y*step-pos.z)).sqrMagnitude));}}
 // Reserve capacity for the nearest candidates, evicting only intact distant trees.
 if(pending.Count>0 && live.Count>=maxLiveTrees){
 float nearest=DistanceSq(pending[0],pos);Vector2Int victim=default;float farthest=nearest+256;bool found=false;
 foreach(var pair in live){if(!pair.Value||pair.Value.GetComponent<TreeDamagePreview>().State!=0)continue;float d=DistanceSq(pair.Key,pos);if(d>farthest){farthest=d;victim=pair.Key;found=true;}}
 if(found){live[victim].SetActive(false);Destroy(live[victim]);live.Remove(victim);colliderCache.Remove(victim);visited.Remove(victim);if(far)far.Dirty(victim);}}
 NearPending=pending.Count>0 && DistanceSq(pending[0],pos)<100*100;
 float deadline=Time.realtimeSinceStartup+.002f; int count=0;while(pending.Count>0&&live.Count<maxLiveTrees&&count++<Mathf.Min(spawnBudget,4)&&Time.realtimeSinceStartup<deadline){var k=pending[0];pending.RemoveAt(0);if(visited.Contains(k))continue;float x=k.x*step,z=k.y*step;Terrain best=null;
 foreach(var t in terrains){if(!t||!t.drawHeightmap||!t.terrainData)continue;var a=t.GetPosition();var s=t.terrainData.size;if(x<a.x||z<a.z||x>=a.x+s.x||z>=a.z+s.z)continue;if(!best||t.terrainData.heightmapResolution>best.terrainData.heightmapResolution)best=t;}
 if(!best){missing++;retryAfter[k]=Time.unscaledTime+.5f;continue;}var p=best.GetPosition();var data=best.terrainData;float u=(x-p.x)/data.size.x,v=(z-p.z)/data.size.z;float expected=p.y+data.GetInterpolatedHeight(u,v);if(DistanceSq(k,pos)>range*range)continue;if(!TryGround(x,z,expected,out float ground,out Vector3 normal)){groundMissing++;retryAfter[k]=Time.unscaledTime+.5f;continue;}if(Vector3.Angle(normal,Vector3.up)>maxSlope){visited.Add(k);rejected++;continue;}
 retryAfter.Remove(k);visited.Add(k);
 using(var spawn=ForestMetrics.Measure(4)){int species=SpeciesIndex(k);float baseY=Bases[species];var instance=Instantiate(Species[species].gameObject,staging);instance.SetActive(false);instance.transform.SetParent(transform,false);var d=instance.GetComponent<TreeDamagePreview>();d.forestInstance=true;instance.transform.rotation=Quaternion.Euler(0,Unit(Hash(k.x,k.y,seed+1))*360,0);instance.transform.localScale=Vector3.one*d.treeScale*Mathf.Lerp(minimumScale,maximumScale,Unit(Hash(k.x,k.y,seed+2)));instance.transform.position=new Vector3(x,ground-baseY*instance.transform.localScale.y+d.groundOffset-groundSinkDepth-.03f,z);d.wood.SetActive(true);d.leaves.SetActive(true);foreach(var c in instance.GetComponentsInChildren<Collider>(true))c.enabled=(instance.transform.position-pos).sqrMagnitude<colliderRadius*colliderRadius;var lod=instance.GetComponent<TreeDistanceLod>();if(lod)lod.ConfigureForest();
 colliderCache[k]=instance.GetComponentsInChildren<Collider>(true);instance.SetActive(true);if(lod && Camera.main)lod.Tick(Camera.main);live[k]=instance; if(far)far.Dirty(k); spawned++; if(source.activeSelf){source.SetActive(false); Debug.Log("[Vegetation] Forest: first tree placed.");}}
 }
 if(Time.unscaledTime>=nextReport){nextReport=Time.unscaledTime+5;Debug.Log($"[Forest] tiles={terrains.Length}, pending={pending.Count}, spawned={spawned}, slopeRejected={rejected}, missingTerrain={missing}, player={pos}, live={live.Count}, groundMissing={groundMissing}, radius={range}, density={treesPerHectare*.1625f}, nearPending={NearPending}, cap={maxLiveTrees}");}
 }
 float profileStart; int frames; float frameSum, worst;
 void ReportProfile(){float dt=Time.unscaledDeltaTime;frames++;frameSum+=dt;worst=Mathf.Max(worst,dt);if(Time.unscaledTime-profileStart<5)return;profileStart=Time.unscaledTime;
 int[] levels=new int[6];long triangles=0;int colliders=0,renderers=0;
 foreach(var pair in live){var go=pair.Value;if(!go||!go.activeInHierarchy)continue;var lod=go.GetComponent<TreeDistanceLod>();if(lod)levels[Mathf.Clamp(lod.CurrentLod,0,5)]++;foreach(var f in go.GetComponentsInChildren<MeshFilter>()){var r=f.GetComponent<MeshRenderer>();if(!r||!r.enabled||!f.sharedMesh)continue;renderers++;for(int i=0;i<f.sharedMesh.subMeshCount;i++)if(f.sharedMesh.GetTopology(i)==MeshTopology.Triangles)triangles+=(long)f.sharedMesh.GetIndexCount(i)/3;}if(colliderCache.TryGetValue(pair.Key,out var cs))foreach(var c in cs)if(c&&c.enabled&&c.gameObject.activeInHierarchy)colliders++;}
 Debug.Log($"[ForestProfiler] FPS avg={frames/Mathf.Max(.001f,frameSum):F1}, min={1/Mathf.Max(.001f,worst):F1}, frame avg={1000*frameSum/Mathf.Max(1,frames):F2}ms, max={worst*1000:F2}ms; live={live.Count}, LOD0/1/2/3/4/hidden={string.Join("/",levels)}, enabledTriangles={triangles}, renderers={renderers}, colliders={colliders}; {ForestMetrics.Report(frames)}");frames=0;frameSum=worst=0;}
 bool TryGround(float x,float z,float expected,out float ground,out Vector3 normal){
 using(var sample=ForestMetrics.Measure(5)){ground=0;normal=Vector3.up;float error=float.PositiveInfinity;int resolution=-1;
 int hits=Physics.RaycastNonAlloc(new Vector3(x,expected+64,z),Vector3.down,groundHits,128,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore);
 for(int i=0;i<hits;i++){var hit=groundHits[i];if(!hit.collider||hit.normal.y<=.05f)continue;
 int rank=-1;if(hit.collider is TerrainCollider tc){var t=tc.GetComponent<Terrain>();if(t&&t.drawHeightmap&&t.terrainData)rank=t.terrainData.heightmapResolution;}
 else if(hit.collider is MeshCollider && hit.collider.GetComponentInParent<Unlimited.HybridWorld.HybridDensityPatchProbe>())rank=int.MaxValue;
 if(rank<0)continue;float e=Mathf.Abs(hit.point.y-expected);if(rank>resolution||(rank==resolution&&e<error)){resolution=rank;error=e;ground=hit.point.y;normal=hit.normal;}}
 return resolution>=0;}
 }

}
}
