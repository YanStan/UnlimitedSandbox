using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
namespace Unlimited.Vegetation {
// Nested meshes: each transition ends at the exact geometry of the next mesh.
internal static class TreeMorphCache {
 static readonly Dictionary<(Mesh,Mesh,Mesh,Mesh,bool),Mesh[]> cache=new();
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
 static void Reset(){foreach(var chain in cache.Values)foreach(var m in chain)if(m)UnityEngine.Object.Destroy(m);cache.Clear();}
 sealed class Data {public Vector3[] v,n;public Vector2[] uv;public int[] t;}
 static Data Read(Mesh m)=>new Data{v=m.vertices,n=m.normals,uv=m.uv,t=m.triangles};
 static Mesh Make(Data d,Vector3[] target,Vector3[] normals,Bounds bounds){var m=new Mesh{name="Nested tree LOD"};if(d.v.Length>65535)m.indexFormat=IndexFormat.UInt32;m.vertices=d.v;m.normals=d.n;m.uv=d.uv;m.triangles=d.t;if(target!=null){m.SetUVs(1,new List<Vector3>(target));m.SetUVs(2,new List<Vector3>(normals));}m.bounds=bounds;return m;}
 public static Mesh[] Get(Mesh[] source,bool leaf){for(int i=0;i<4;i++)if(!source[i]||!source[i].isReadable||source[i].subMeshCount!=1)return null;var key=(source[0],source[1],source[2],source[3],leaf);if(cache.TryGetValue(key,out var found))return found;
 var d=Read(source[0]);if(d.n.Length!=d.v.Length||d.uv.Length!=d.v.Length)return null;var result=new Mesh[4];var bounds=source[0].bounds;
 for(int l=0;l<3;l++){int goal=Mathf.Max(1,(int)source[l+1].GetIndexCount(0)/3);Vector3[] target,normals;Data next;if(leaf)next=Thin(d,goal,out target,out normals);else next=Reduce(d,goal,bounds,out target,out normals);result[l]=Make(d,target,normals,bounds);d=next;}
 result[3]=Make(d,null,null,bounds);cache.Add(key,result);Debug.Log("[TreeMorph] Nested chain prepared: "+source[0].name);return result;}
 static Data Reduce(Data d,int goal,Bounds bounds,out Vector3[] target,out Vector3[] normals){float lo=0,hi=bounds.size.magnitude;Data best=d;int[] bestMap=new int[d.v.Length];for(int i=0;i<bestMap.Length;i++)bestMap[i]=i;int err=Math.Abs(d.t.Length/3-goal);
 for(int k=0;k<16;k++){float step=Mathf.Max(.00001f,(lo+hi)*.5f);var candidate=Cluster(d,step,bounds.min,out var map);int count=candidate.t.Length/3;int e=Math.Abs(count-goal);if(count>0&&e<err){best=candidate;bestMap=map;err=e;}if(count>goal)lo=step;else hi=step;}
 target=new Vector3[d.v.Length];normals=new Vector3[d.v.Length];for(int i=0;i<target.Length;i++){target[i]=best.v[bestMap[i]];normals[i]=best.n[bestMap[i]];}return best;}
 static Data Cluster(Data d,float step,Vector3 min,out int[] map){var keys=new Dictionary<Vector3Int,int>();var v=new List<Vector3>();var n=new List<Vector3>();var uv=new List<Vector2>();var counts=new List<int>();map=new int[d.v.Length];for(int i=0;i<map.Length;i++){var q=(d.v[i]-min)/step;var key=new Vector3Int(Mathf.FloorToInt(q.x),Mathf.FloorToInt(q.y),Mathf.FloorToInt(q.z));if(!keys.TryGetValue(key,out int j)){j=v.Count;keys.Add(key,j);v.Add(Vector3.zero);n.Add(Vector3.zero);uv.Add(Vector2.zero);counts.Add(0);}map[i]=j;v[j]+=d.v[i];n[j]+=d.n[i];uv[j]+=d.uv[i];counts[j]++;}for(int j=0;j<v.Count;j++){v[j]/=counts[j];n[j]=n[j].sqrMagnitude>1e-10f?n[j].normalized:Vector3.up;uv[j]/=counts[j];}var t=new List<int>();var seen=new HashSet<Vector3Int>();for(int i=0;i<d.t.Length;i+=3){int a=map[d.t[i]],b=map[d.t[i+1]],c=map[d.t[i+2]];if(a==b||b==c||a==c)continue;int x=Mathf.Min(a,Mathf.Min(b,c)),z=Mathf.Max(a,Mathf.Max(b,c));if(!seen.Add(new Vector3Int(x,a+b+c-x-z,z)))continue;t.Add(a);t.Add(b);t.Add(c);}return new Data{v=v.ToArray(),n=n.ToArray(),uv=uv.ToArray(),t=t.ToArray()};}
 static int Find(int[] p,int i){while(p[i]!=i){p[i]=p[p[i]];i=p[i];}return i;}
 static Data Thin(Data d,int goal,out Vector3[] target,out Vector3[] normals){int[] parent=new int[d.v.Length];for(int i=0;i<parent.Length;i++)parent[i]=i;for(int i=0;i<d.t.Length;i+=3){int a=Find(parent,d.t[i]);parent[Find(parent,d.t[i+1])]=a;parent[Find(parent,d.t[i+2])]=a;}var groups=new Dictionary<int,List<int>>();for(int i=0;i<d.t.Length;i+=3){int k=Find(parent,d.t[i]);if(!groups.TryGetValue(k,out var g))groups[k]=g=new List<int>();g.Add(d.t[i]);g.Add(d.t[i+1]);g.Add(d.t[i+2]);}var cards=new List<List<int>>(groups.Values);var rng=new System.Random(419);for(int i=cards.Count-1;i>0;i--){int j=rng.Next(i+1);(cards[i],cards[j])=(cards[j],cards[i]);}target=(Vector3[])d.v.Clone();normals=(Vector3[])d.n.Clone();var keep=new List<int>();foreach(var card in cards){if(keep.Count==0||keep.Count+card.Count<=goal*3){keep.AddRange(card);continue;}Vector3 center=Vector3.zero;foreach(int i in card)center+=d.v[i];center/=card.Count;foreach(int i in card)target[i]=center;}return new Data{v=d.v,n=d.n,uv=d.uv,t=keep.ToArray()};}
}
}
