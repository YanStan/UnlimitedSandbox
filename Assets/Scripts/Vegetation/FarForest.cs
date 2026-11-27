using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Unlimited.Vegetation {
// One combined billboard mesh per spatial tile; no per-tree GameObjects.
public sealed class FarForest : MonoBehaviour {
 const float Tile=64f;
 public float farRadius=2000f;
 public int chunksPerFrame=4;
 ForestPreview owner; Material[] materials; float[] extents; Vector3[] centers;
 sealed class Chunk {public Mesh mesh; public int count; public int terrainVersion; public int[] counts;}
 readonly Dictionary<Vector2Int,Chunk> chunks=new Dictionary<Vector2Int,Chunk>();
 readonly HashSet<Vector2Int> dirty=new HashSet<Vector2Int>();
 readonly List<Vector2Int> queue=new List<Vector2Int>(), remove=new List<Vector2Int>();
 Terrain[] terrains; int terrainVersion, signature; float nextScan,nextLog; Vector3 previous, direction;
 public void Initialize(ForestPreview o){owner=o;int n=o.Species.Count;materials=new Material[n];extents=new float[n];centers=new Vector3[n];var shader=Shader.Find("Unlimited/Vegetation/FarForest");if(!shader||!shader.isSupported){enabled=false;return;}for(int i=0;i<n;i++){var t=o.Species[i];var lod=t.GetComponent<TreeDistanceLod>();if(!lod||!lod.impostor){enabled=false;return;}var mf=lod.impostor.GetComponent<MeshFilter>();if(!mf||!mf.sharedMesh||!lod.impostor.sharedMaterial){enabled=false;return;}extents[i]=mf.sharedMesh.bounds.extents.x;centers[i]=t.transform.InverseTransformPoint(lod.impostor.transform.position);materials[i]=new Material(shader);materials[i].SetTexture("_MainTex",lod.impostor.sharedMaterial.GetTexture("_MainTex"));}}
 Vector2Int Key(float x,float z)=>new Vector2Int(Mathf.FloorToInt(x/Tile),Mathf.FloorToInt(z/Tile));
 public void Dirty(Vector2Int k){if(owner){var tile=Key(k.x*owner.GridStep,k.y*owner.GridStep);if(chunks.ContainsKey(tile))dirty.Add(tile);}}
 Vector3 Middle(Vector2Int k)=>new Vector3((k.x+.5f)*Tile,0,(k.y+.5f)*Tile);
 void LateUpdate(){if(!owner||materials==null||!owner.PlayerTransform)return;var p=owner.PlayerTransform.position; p.y=0;float radius=Mathf.Min(farRadius,owner.radius);
 if(!ForestBenchmark.Running){if(terrains==null || Time.unscaledTime>=nextScan || (p-previous).sqrMagnitude>32*32){nextScan=Time.unscaledTime+.25f;var delta=p-previous;if(delta.sqrMagnitude>1)direction=delta.normalized;previous=p;terrains=Terrain.activeTerrains;int sig=0;unchecked{foreach(var t in terrains)if(t&&t.terrainData)sig+=t.GetEntityId().GetHashCode()*31+t.terrainData.GetEntityId().GetHashCode()+t.terrainData.heightmapResolution+(t.drawHeightmap?1:0);}if(sig!=signature){signature=sig;terrainVersion++;}
 queue.Clear();remove.Clear();foreach(var kv in chunks)if((Middle(kv.Key)-p).sqrMagnitude>(radius+96)*(radius+96))remove.Add(kv.Key);foreach(var k in remove){Destroy(chunks[k].mesh);chunks.Remove(k);dirty.Remove(k);}
 var lo=Key(p.x-radius-48,p.z-radius-48);var hi=Key(p.x+radius+48,p.z+radius+48);
 for(int z=lo.y;z<=hi.y;z++)for(int x=lo.x;x<=hi.x;x++){var k=new Vector2Int(x,z);if((Middle(k)-p).sqrMagnitude>(radius+48)*(radius+48))continue;if(!chunks.TryGetValue(k,out var c)||c.terrainVersion!=terrainVersion||dirty.Contains(k))queue.Add(k);}
 queue.Sort((a,b)=>Priority(a,p).CompareTo(Priority(b,p)));}
 // Near replacements are rebuilt first, so a live tree does not retain a duplicate billboard.
 int budget=Mathf.Max(1,chunksPerFrame);remove.Clear();foreach(var k in dirty){if(chunks.ContainsKey(k))remove.Add(k);}
 remove.Sort((a,b)=>(Middle(a)-p).sqrMagnitude.CompareTo((Middle(b)-p).sqrMagnitude));
 float dirtyDeadline=Time.realtimeSinceStartup+.002f;
 foreach(var k in remove){if(budget<=0||Time.realtimeSinceStartup>=dirtyDeadline)break;Build(k);budget--;}
 if(owner.NearPending)budget=0;
 float deadline=Time.realtimeSinceStartup+.003f;while(budget-->0&&queue.Count>0&&Time.realtimeSinceStartup<deadline){var k=queue[0];queue.RemoveAt(0);if(chunks.TryGetValue(k,out var c)&&c.terrainVersion==terrainVersion&&!dirty.Contains(k))continue;Build(k);}}
 foreach(var material in materials){material.SetVector("_ForestCenter",p);material.SetFloat("_ForestRadius",radius);}
 int total=0,draws=0;foreach(var kv in chunks){var c=kv.Value;if(c.count==0||(Middle(kv.Key)-p).sqrMagnitude>(radius+48)*(radius+48))continue;for(int i=0;i<materials.Length;i++){if(c.counts[i]==0)continue;Graphics.DrawMesh(c.mesh,Matrix4x4.identity,materials[i],gameObject.layer,null,i,null,ShadowCastingMode.Off,false);draws++;}total+=c.count;}
 if(Time.unscaledTime>=nextLog){nextLog=Time.unscaledTime+5;Debug.Log($"[FarForest] chunks={chunks.Count}, submittedChunks={draws}, billboards={total}, triangles={total*2}, pending={queue.Count}, radius={radius}, density={owner.treesPerHectare*.1625f}");}}
 float Priority(Vector2Int k,Vector3 p){var d=Middle(k)-p;float distance=d.magnitude;return distance<320?distance:320+distance-Vector3.Dot(d,direction)*.55f;}
 void Build(Vector2Int k){dirty.Remove(k);if(!chunks.TryGetValue(k,out var c)){c=new Chunk{mesh=new Mesh{name="Far forest tile"}};chunks.Add(k,c);}c.terrainVersion=terrainVersion;c.mesh.Clear();c.count=0;var vs=new List<Vector3>();var uv=new List<Vector2>();var info=new List<Vector4>();var indices=new List<int>[materials.Length];c.counts=new int[materials.Length];for(int i=0;i<indices.Length;i++)indices[i]=new List<int>();float step=owner.GridStep;float x0=k.x*Tile,z0=k.y*Tile;
 if(terrains==null)return;
 for(int z=Mathf.CeilToInt(z0/step);z*step<z0+Tile;z++)for(int x=Mathf.CeilToInt(x0/step);x*step<x0+Tile;x++){var key=new Vector2Int(x,z);if(owner.SuppressFar(key)||ForestPreview.Unit(ForestPreview.Hash(x,z,owner.seed))>=Mathf.Min(1,owner.treesPerHectare*.1625f*step*step/10000f))continue;float wx=x*step,wz=z*step;Terrain best=null;foreach(var t in terrains){if(!t||!t.drawHeightmap||!t.terrainData)continue;var a=t.GetPosition();var s=t.terrainData.size;if(wx<a.x||wz<a.z||wx>=a.x+s.x||wz>=a.z+s.z)continue;if(!best||t.terrainData.heightmapResolution>best.terrainData.heightmapResolution)best=t;}if(!best)continue;var pos=best.GetPosition();var data=best.terrainData;float u=(wx-pos.x)/data.size.x,v=(wz-pos.z)/data.size.z;if(data.GetSteepness(u,v)>owner.maxSlope)continue;int species=owner.SpeciesIndex(key);var template=owner.Species[species];float baseY=owner.Bases[species],extent=extents[species];var center=centers[species];float scale=template.treeScale*Mathf.Lerp(owner.minimumScale,owner.maximumScale,ForestPreview.Unit(ForestPreview.Hash(x,z,owner.seed+2)));float yaw=ForestPreview.Unit(ForestPreview.Hash(x,z,owner.seed+1))*360;var root=new Vector3(wx,pos.y+data.GetInterpolatedHeight(u,v)-baseY*scale+template.groundOffset-owner.groundSinkDepth-.03f,wz);var mid=root+Quaternion.Euler(0,yaw,0)*(center*scale);int i=vs.Count;for(int j=0;j<4;j++){vs.Add(mid);uv.Add(new Vector2(j==1||j==2?1:0,j>=2?1:0));info.Add(new Vector4(extent*scale,yaw,wx,wz));}indices[species].Add(i);indices[species].Add(i+1);indices[species].Add(i+2);indices[species].Add(i);indices[species].Add(i+2);indices[species].Add(i+3);c.count++;c.counts[species]++;}
 c.mesh.SetVertices(vs);c.mesh.SetUVs(0,uv);c.mesh.SetUVs(1,info);c.mesh.subMeshCount=indices.Length;for(int j=0;j<indices.Length;j++)c.mesh.SetTriangles(indices[j],j);if(c.count>0){var b=c.mesh.bounds;float e=0;for(int j=0;j<extents.Length;j++)e=Mathf.Max(e,extents[j]*owner.Species[j].treeScale*Mathf.Max(owner.minimumScale,owner.maximumScale));b.Expand(2*e+2);c.mesh.bounds=b;}}
 void OnDestroy(){foreach(var c in chunks.Values)if(c.mesh)Destroy(c.mesh);if(materials!=null)foreach(var material in materials)if(material)Destroy(material);}
}
}
