using UnityEngine;
using UnityEditor;
using Unlimited.Vegetation;
public static class MixedTreeBuilder {
 const string Root="Assets/Scripts/Vegetation/";
 [MenuItem("Unlimited/Vegetation/Build Mixed Tree Prefabs")]
 public static void Build(){
 if(EditorApplication.isPlaying){Debug.LogError("Exit Play Mode first");return;}
 var previous=Selection.activeObject;
 if(!AssetDatabase.IsValidFolder(Root+"MixedPrefabs"))AssetDatabase.CreateFolder(Root.TrimEnd('/'),"MixedPrefabs");
 foreach(string species in new[]{"Birch","Maple","Willow"}){
 GameObject tree=null; TreeLodBaker baker=null;
 try{
 var shader=Shader.Find("Unlimited/Vegetation/"+species);if(!shader)throw new System.Exception("Missing shader "+species);
 tree=new GameObject(species+" Preview");
 var d=tree.AddComponent<TreeDamagePreview>();d.wood=Part(tree,species,"Trunk",shader,false);d.leaves=Part(tree,species,"Leaves",shader,true);
 foreach(var f in d.wood.GetComponentsInChildren<MeshFilter>()){var c=f.gameObject.AddComponent<MeshCollider>();c.sharedMesh=f.sharedMesh;}
 Selection.activeGameObject=tree;baker=ScriptableObject.CreateInstance<TreeLodBaker>();baker.Bake();
 var lod=tree.GetComponent<TreeDistanceLod>();if(!lod||lod.parts==null||lod.parts.Length==0)throw new System.Exception("LOD bake failed");
 TreeImpostorBaker.Bake();if(!lod.impostor)throw new System.Exception("Impostor bake failed");
 PrefabUtility.SaveAsPrefabAsset(tree,Root+"MixedPrefabs/"+species+".prefab");Debug.Log("[MixedTrees] Saved "+species);
 }catch(System.Exception e){Debug.LogException(e);}finally{if(baker)Object.DestroyImmediate(baker);if(tree)Object.DestroyImmediate(tree);}
 }Selection.activeObject=previous;AssetDatabase.SaveAssets();
 }
 static GameObject Part(GameObject tree,string species,string name,Shader shader,bool foliage){
 string path=Root+"Models/Mixed/"+species+"_"+name+".obj";
 var importer=AssetImporter.GetAtPath(path) as ModelImporter;if(importer==null)throw new System.Exception("Missing "+path);
 if(!importer.isReadable){importer.isReadable=true;importer.SaveAndReimport();}
 var source=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!source)throw new System.Exception("Missing model "+path);
 var part=(GameObject)PrefabUtility.InstantiatePrefab(source);part.name=name;part.transform.SetParent(tree.transform,false);
 string mp=Root+"MixedPrefabs/"+species+"_"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(mp);
 if(!mat){mat=new Material(shader);AssetDatabase.CreateAsset(mat,mp);}mat.shader=shader;mat.SetFloat("_Foliage",foliage?1:0);mat.SetFloat("_Wind",0);mat.enableInstancing=true;EditorUtility.SetDirty(mat);
 foreach(var r in part.GetComponentsInChildren<MeshRenderer>()){var mats=r.sharedMaterials;for(int i=0;i<mats.Length;i++)mats[i]=mat;r.sharedMaterials=mats;}return part;
 }
}
