using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unlimited.Vegetation;
using System.Collections.Generic;
public static class ForestPaletteRefresh {
 [MenuItem("Unlimited/Vegetation/Refresh Forest Colors")]
 public static void Refresh(){
 if(EditorApplication.isPlaying){Debug.LogError("Exit Play Mode first");return;}
 var forests=Object.FindObjectsByType<ForestPreview>(FindObjectsInactive.Include);
 var trees=new HashSet<TreeDamagePreview>();
 foreach(var f in forests){if(f.template)trees.Add(f.template);else foreach(var t in Object.FindObjectsByType<TreeDamagePreview>(FindObjectsInactive.Include))if(t.gameObject.scene==f.gameObject.scene && !t.forestInstance && t.wood && t.leaves && t.GetComponent<TreeDistanceLod>()){trees.Add(t);break;}if(f.mixedTemplates!=null)foreach(var t in f.mixedTemplates)if(t)trees.Add(t);}
 if(trees.Count==0){Debug.LogError("[ForestColors] No tree templates found");return;}
 var previous=Selection.activeObject;int count=0;
 try{foreach(var t in trees){if(!t)continue;GameObject temp=null;try{
 var root=t.gameObject;string path=null;
 if(EditorUtility.IsPersistent(root)){path=AssetDatabase.GetAssetPath(root);temp=(GameObject)PrefabUtility.InstantiatePrefab(root);root=temp;}
 var lod=root.GetComponent<TreeDistanceLod>();if(!lod)throw new System.Exception("Missing LOD: "+root.name);
 var old=lod.impostor;Selection.activeGameObject=root;TreeImpostorBaker.Bake();
 if(!lod.impostor || lod.impostor==old)throw new System.Exception("Impostor bake failed: "+root.name);
 if(temp){if(!PrefabUtility.SaveAsPrefabAsset(root,path))throw new System.Exception("Prefab save failed: "+path);}else{EditorUtility.SetDirty(lod);EditorSceneManager.MarkSceneDirty(root.scene);}
 count++;}catch(System.Exception e){Debug.LogException(e);}finally{if(temp)Object.DestroyImmediate(temp);}}
 }finally{Selection.activeObject=previous;AssetDatabase.SaveAssets();}
 Debug.Log("[ForestColors] Refreshed "+count+" / "+trees.Count+" tree impostors. Save scene.");
 }
}
