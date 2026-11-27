using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Unlimited.Vegetation;

public sealed class TreeLodBaker : EditorWindow
{
    float l1=.5f,l2=.2f,l3=.05f;
    [MenuItem("Unlimited/Vegetation/Bake Selected Tree LODs")]
    static void Open() { GetWindow<TreeLodBaker>("Tree LOD Baker"); }
    void OnGUI()
    {
        EditorGUILayout.HelpBox("Select Oak Preview in Hierarchy outside Play Mode. Original colliders and materials remain unchanged.",MessageType.Info);
        l1=EditorGUILayout.Slider("LOD1 fraction",l1,.05f,1);
        l2=EditorGUILayout.Slider("LOD2 fraction",l2,.02f,l1);
        l3=EditorGUILayout.Slider("LOD3 fraction",l3,.01f,l2);
        if(GUILayout.Button("Bake selected tree")) Bake();
    }
    public void Bake()
    {
        if(EditorApplication.isPlaying) { Debug.LogError("Exit Play Mode first."); return; }
        var root=Selection.activeGameObject;
        var tree=root ? root.GetComponent<TreeDamagePreview>() : null;
        if(!tree || !tree.wood || !tree.leaves) { Debug.LogError("Select Oak Preview root."); return; }
        var old=root.GetComponent<TreeDistanceLod>();
        var parts=new List<TreeDistanceLod.Part>();
        foreach(var part in new[]{tree.wood,tree.leaves}) foreach(var f in part.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m=f.sharedMesh;
            if(old && old.parts!=null) foreach(var p in old.parts) if(p.filter==f && p.meshes!=null && p.meshes.Length>0) m=p.meshes[0];
            if(!m || !m.isReadable || m.subMeshCount!=1 || m.GetTopology(0)!=MeshTopology.Triangles) { Debug.LogError("Need readable single-submesh triangle mesh: "+f.name); return; }
            parts.Add(new TreeDistanceLod.Part{filter=f,meshes=new Mesh[]{m,null,null,null}});
        }
        const string folder="Assets/Scripts/Vegetation/GeneratedLOD";
        if(!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/Scripts/Vegetation","GeneratedLOD");
        float[] fractions={l1,l2,l3};
        foreach(var p in parts)
        {
            var src=p.meshes[0]; bool foliage=p.filter.transform.IsChildOf(tree.leaves.transform);
            for(int i=1;i<=3;i++)
            {
                int target=Mathf.Max(1,Mathf.RoundToInt(src.triangles.Length/3*fractions[i-1]));
                Mesh m=foliage?ThinCards(src,target):Simplify(src,target);
                m.name=src.name+"_LOD"+i;
                AssetDatabase.CreateAsset(m,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+m.name+".asset"));
                p.meshes[i]=m;
                Debug.Log($"[Vegetation LOD] {m.name}: {m.triangles.Length/3} triangles (target {target})");
            }
        }
        var lod=old?old:Undo.AddComponent<TreeDistanceLod>(root);
        Undo.RecordObject(lod,"Bake tree LODs"); lod.parts=parts.ToArray(); EditorUtility.SetDirty(lod);
        AssetDatabase.SaveAssets(); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
    }
    static Mesh Make(Mesh src,List<Vector3> v,List<Vector3> n,List<Vector2> uv,List<int> t)
    {
        var m=new Mesh(); if(v.Count>65535)m.indexFormat=IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0,uv); m.SetTriangles(t,0); m.bounds=src.bounds; return m;
    }
    static Mesh ThinCards(Mesh src,int target)
    {
        var v=src.vertices; var n=src.normals; var uv=src.uv; var t=src.triangles;
        int[] parent=new int[v.Length]; for(int i=0;i<parent.Length;i++)parent[i]=i;
        int Find(int x){while(parent[x]!=x){parent[x]=parent[parent[x]];x=parent[x];}return x;}
        for(int i=0;i<t.Length;i+=3){parent[Find(t[i+1])]=Find(t[i]);parent[Find(t[i+2])]=Find(t[i]);}
        var groups=new Dictionary<int,List<int>>();
        for(int i=0;i<t.Length;i+=3){int k=Find(t[i]);if(!groups.TryGetValue(k,out var g))groups[k]=g=new List<int>();g.Add(t[i]);g.Add(t[i+1]);g.Add(t[i+2]);}
        var cards=new List<List<int>>(groups.Values); var rng=new System.Random(419);
        for(int i=cards.Count-1;i>0;i--){int j=rng.Next(i+1);var tmp=cards[i];cards[i]=cards[j];cards[j]=tmp;}
        var chosen=new List<int>(); foreach(var c in cards){if(chosen.Count==0 || chosen.Count+c.Count<=target*3)chosen.AddRange(c);}
        return Make(src,new List<Vector3>(v),new List<Vector3>(n),new List<Vector2>(uv),chosen);
    }
    static Mesh Simplify(Mesh src,int target)
    {
        float lo=0,hi=src.bounds.size.magnitude; Mesh best=null; int error=int.MaxValue;
        for(int i=0;i<22;i++)
        {
            float step=(lo+hi)*.5f; var m=Cluster(src,Mathf.Max(step,.00001f)); int count=m.triangles.Length/3;
            if(count>0 && Mathf.Abs(count-target)<error){if(best)DestroyImmediate(best);best=m;error=Mathf.Abs(count-target);}else DestroyImmediate(m);
            if(count>target)lo=step;else hi=step;
        }
        return best?best:Instantiate(src);
    }
    static Mesh Cluster(Mesh src,float step)
    {
        var v=src.vertices;var n=src.normals;var uv=src.uv;var t=src.triangles;
        var map=new Dictionary<Vector3Int,int>();var vs=new List<Vector3>();var ns=new List<Vector3>();var us=new List<Vector2>();var counts=new List<int>();int[] remap=new int[v.Length];
        for(int i=0;i<v.Length;i++)
        {
            var q=(v[i]-src.bounds.min)/step;var key=new Vector3Int(Mathf.FloorToInt(q.x),Mathf.FloorToInt(q.y),Mathf.FloorToInt(q.z));
            if(!map.TryGetValue(key,out int j)){j=vs.Count;map.Add(key,j);vs.Add(Vector3.zero);ns.Add(Vector3.zero);us.Add(Vector2.zero);counts.Add(0);}
            remap[i]=j;vs[j]+=v[i];if(n.Length==v.Length)ns[j]+=n[i];if(uv.Length==v.Length)us[j]+=uv[i];counts[j]++;
        }
        for(int i=0;i<vs.Count;i++){vs[i]/=counts[i];ns[i]=ns[i].normalized;us[i]/=counts[i];}
        var ts=new List<int>();var seen=new HashSet<Vector3Int>();
        for(int i=0;i<t.Length;i+=3){int a=remap[t[i]],b=remap[t[i+1]],c=remap[t[i+2]];if(a==b||b==c||a==c)continue;int min=Mathf.Min(a,Mathf.Min(b,c)),max=Mathf.Max(a,Mathf.Max(b,c));if(!seen.Add(new Vector3Int(min,a+b+c-min-max,max)))continue;ts.Add(a);ts.Add(b);ts.Add(c);}
        return Make(src,vs,ns,us,ts);
    }
}
